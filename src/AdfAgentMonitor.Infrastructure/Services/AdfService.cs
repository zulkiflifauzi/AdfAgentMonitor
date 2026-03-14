using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.DataFactory;
using Azure.ResourceManager.DataFactory.Models;
using AdfAgentMonitor.Core.Entities;
using AdfAgentMonitor.Core.Enums;
using AdfAgentMonitor.Core.Interfaces;
using AdfAgentMonitor.Infrastructure.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AdfAgentMonitor.Infrastructure.Services;

public class AdfService(
    ArmClient armClient,
    IOptions<AdfSettings> options,
    ILogger<AdfService> logger) : IAdfService
{
    private readonly AdfSettings _settings  = options.Value;
    private readonly ArmClient   _armClient = armClient;

    /// <inheritdoc/>
    public async Task<IEnumerable<PipelineRunState>> GetFailedRunsAsync(CancellationToken ct = default)
    {
        try
        {
            var factory = await GetFactoryResourceAsync(ct);

            var filter = new RunFilterContent(
                DateTimeOffset.UtcNow.AddMinutes(-_settings.LookbackMinutes),
                DateTimeOffset.UtcNow);

            filter.Filters.Add(new RunQueryFilter(
                RunQueryFilterOperand.Status,
                RunQueryFilterOperator.EqualsValue,
                ["Failed"]));

            var runs = new List<PipelineRunState>();

            await foreach (var run in factory.GetPipelineRunsAsync(filter, ct))
            {
                runs.Add(new PipelineRunState
                {
                    Id            = Guid.NewGuid(),
                    PipelineRunId = run.RunId?.ToString() ?? string.Empty,
                    PipelineName  = run.PipelineName ?? string.Empty,
                    FactoryName   = _settings.FactoryName,
                    Status        = PipelineRunStatus.Failed,
                    FailedAt      = run.RunEndOn,
                    CreatedAt     = DateTimeOffset.UtcNow,
                    UpdatedAt     = DateTimeOffset.UtcNow
                });
            }

            logger.LogInformation(
                "Found {Count} failed run(s) in factory {FactoryName} over the last {Minutes} minutes.",
                runs.Count, _settings.FactoryName, _settings.LookbackMinutes);

            return runs;
        }
        catch (RequestFailedException ex)
        {
            logger.LogError(ex,
                "ADF API request failed while fetching runs for factory {FactoryName}.",
                _settings.FactoryName);
            return [];
        }
    }

    /// <inheritdoc/>
    public async Task<string> GetActivityRunLogsAsync(
        string pipelineRunId,
        CancellationToken ct = default)
    {
        try
        {
            var factory = await GetFactoryResourceAsync(ct);

            // The date range must encompass the run; using a 7-day window is safe and
            // the pipelineRunId filter in the API scopes results to the specific run.
            var filter = new RunFilterContent(
                DateTimeOffset.UtcNow.AddDays(-7),
                DateTimeOffset.UtcNow);

            var lines = new List<string>();

            await foreach (var activity in factory.GetActivityRunAsync(pipelineRunId, filter, ct))
            {
                var errorText = activity.Error?.ToString() ?? string.Empty;
                lines.Add(
                    $"[{activity.ActivityName}] type={activity.ActivityType} " +
                    $"status={activity.Status} " +
                    $"start={activity.StartOn:O} end={activity.EndOn:O}" +
                    (string.IsNullOrEmpty(errorText) ? string.Empty : $" error={errorText}"));
            }

            return string.Join(Environment.NewLine, lines);
        }
        catch (RequestFailedException ex)
        {
            logger.LogError(ex,
                "ADF API request failed while fetching activity logs for run {PipelineRunId}.",
                pipelineRunId);
            return string.Empty;
        }
    }

    /// <inheritdoc/>
    public async Task<string?> TriggerPipelineRerunAsync(
        string pipelineName,
        string originalRunId,
        CancellationToken ct = default)
    {
        try
        {
            var factory = await GetFactoryResourceAsync(ct);

            var pipelineResponse = await factory.GetDataFactoryPipelineAsync(
                pipelineName, ifNoneMatch: null, ct);

            // isRecovery=true + startFromFailure=true re-runs from the last failed activity,
            // preserving the original run's parameter values.
            var result = await pipelineResponse.Value.CreateRunAsync(
                parameterValueSpecification: null,
                referencePipelineRunId:      originalRunId,
                isRecovery:                  true,
                startActivityName:           null,
                startFromFailure:            true,
                cancellationToken:           ct);

            var newRunId = result.Value.RunId.ToString();

            logger.LogInformation(
                "Pipeline {PipelineName} re-triggered from run {OriginalRunId}. New runId={NewRunId}.",
                pipelineName, originalRunId, newRunId);

            return newRunId;
        }
        catch (RequestFailedException ex)
        {
            logger.LogError(ex,
                "Failed to trigger re-run for pipeline {PipelineName} (originalRunId={OriginalRunId}).",
                pipelineName, originalRunId);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> RestartIntegrationRuntimeAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.DefaultIntegrationRuntimeName))
        {
            logger.LogError(
                "Cannot restart Integration Runtime: {Setting} is not configured in AzureDataFactory settings.",
                nameof(AdfSettings.DefaultIntegrationRuntimeName));
            return false;
        }

        try
        {
            var factory = await GetFactoryResourceAsync(ct);

            var irResponse = await factory.GetDataFactoryIntegrationRuntimeAsync(
                _settings.DefaultIntegrationRuntimeName, ifNoneMatch: null, ct);

            // WaitUntil.Started returns as soon as the operation is accepted (HTTP 202).
            // The IR may take several minutes to reach Running state — the caller
            // should not block on full completion.
            await irResponse.Value.StartAsync(WaitUntil.Started, ct);

            logger.LogInformation(
                "Integration Runtime '{IrName}' start operation accepted.",
                _settings.DefaultIntegrationRuntimeName);

            return true;
        }
        catch (RequestFailedException ex)
        {
            logger.LogError(ex,
                "Failed to restart Integration Runtime '{IrName}'.",
                _settings.DefaultIntegrationRuntimeName);
            return false;
        }
    }

    // ---------------------------------------------------------------------------
    // Navigation helper
    // ---------------------------------------------------------------------------

    private async Task<DataFactoryResource> GetFactoryResourceAsync(CancellationToken ct)
    {
        var subIdentifier = new ResourceIdentifier($"/subscriptions/{_settings.SubscriptionId}");
        var subscription  = _armClient.GetSubscriptionResource(subIdentifier);

        var rgResponse      = await subscription.GetResourceGroupAsync(_settings.ResourceGroup, ct);
        var factoryResponse = await rgResponse.Value.GetDataFactoryAsync(_settings.FactoryName, ifNoneMatch: null, ct);

        return factoryResponse.Value;
    }
}
