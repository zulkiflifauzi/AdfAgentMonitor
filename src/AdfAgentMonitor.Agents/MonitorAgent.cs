using AdfAgentMonitor.Core.Entities;
using AdfAgentMonitor.Core.Enums;
using AdfAgentMonitor.Core.Interfaces;
using AdfAgentMonitor.Core.Models;
using Microsoft.Extensions.Logging;

namespace AdfAgentMonitor.Agents;

/// <summary>
/// Polls ADF for failed pipeline runs and inserts a <see cref="PipelineRunState"/> row
/// for each one not already tracked in the database.
/// </summary>
/// <remarks>
/// MonitorAgent is the entry point of the pipeline; it creates state rows rather than
/// processing an existing one. The <see cref="IAgent.ExecuteAsync"/> parameter
/// <c>state</c> is intentionally unused — pass <see langword="null"/> from the
/// Hangfire job shell.
/// </remarks>
public class MonitorAgent(
    IAdfService adfService,
    IPipelineRunStateRepository repository,
    IAgentActivityLogRepository activityLog,
    ILogger<MonitorAgent> logger) : IAgent
{
    // ---------------------------------------------------------------------------
    // IAgent — explicit implementation so the job shell can call the cleaner overload
    // ---------------------------------------------------------------------------

    /// <inheritdoc/>
    /// <param name="state">Not used by MonitorAgent. Pass <see langword="null"/>.</param>
    Task<AgentResult> IAgent.ExecuteAsync(PipelineRunState state, CancellationToken ct)
        => ExecuteAsync(ct);

    // ---------------------------------------------------------------------------
    // Primary entry point (called directly by the Hangfire job shell)
    // ---------------------------------------------------------------------------

    public async Task<AgentResult> ExecuteAsync(CancellationToken ct = default)
    {
        logger.LogInformation("MonitorAgent starting.");

        IEnumerable<PipelineRunState> failedRuns;

        try
        {
            failedRuns = await adfService.GetFailedRunsAsync(ct);
        }
        catch (Exception ex)
        {
            // AdfService swallows RequestFailedException and returns [] — any exception
            // here is unexpected (e.g. misconfiguration, auth failure at startup).
            logger.LogError(ex, "MonitorAgent failed to retrieve runs from ADF.");
            return new AgentResult(
                Success: false,
                Message: $"ADF call failed: {ex.Message}",
                NextStatus: PipelineRunStatus.Failed);
        }

        var newCount = 0;
        var skippedCount = 0;

        foreach (var run in failedRuns)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(run.PipelineRunId))
            {
                logger.LogWarning("Skipping ADF run with empty PipelineRunId.");
                continue;
            }

            if (await repository.ExistsByRunIdAsync(run.PipelineRunId, ct))
            {
                skippedCount++;
                continue;
            }

            await repository.AddAsync(run, ct);
            newCount++;

            logger.LogInformation(
                "Tracked new failed run: pipeline={PipelineName} runId={PipelineRunId} failedAt={FailedAt}",
                run.PipelineName, run.PipelineRunId, run.FailedAt);

            try
            {
                await activityLog.AddAsync(new AgentActivityLog
                {
                    Id             = Guid.NewGuid(),
                    AgentName      = "MonitorAgent",
                    PipelineRunId  = run.Id,
                    PipelineName   = run.PipelineName,
                    Action         = "Detected new failed pipeline run",
                    ResultMessage  = $"Pipeline '{run.PipelineName}' failed at {run.FailedAt:O}. Tracked as runId={run.PipelineRunId}.",
                    Success        = true,
                    Timestamp      = DateTimeOffset.UtcNow,
                }, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "MonitorAgent failed to write activity log for runId={RunId}.", run.PipelineRunId);
            }
        }

        var message = newCount switch
        {
            0 when skippedCount == 0 => "No failed runs found in ADF.",
            0                        => $"No new failures. {skippedCount} already tracked.",
            _                        => $"{newCount} new failure(s) detected. {skippedCount} already tracked."
        };

        logger.LogInformation("MonitorAgent completed. {Message}", message);

        return new AgentResult(
            Success: true,
            Message: message,
            NextStatus: PipelineRunStatus.Failed);
    }
}
