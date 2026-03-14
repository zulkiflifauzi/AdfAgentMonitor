using AdfAgentMonitor.Core.Entities;
using AdfAgentMonitor.Core.Enums;
using AdfAgentMonitor.Core.Interfaces;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace AdfAgentMonitor.Agents;

/// <summary>
/// Orchestrates the full ADF monitoring pipeline via Hangfire.
/// </summary>
/// <remarks>
/// The <c>monitor-adf-pipelines</c> recurring job fires <see cref="MonitorAsync"/>
/// every two minutes. For each newly detected failed pipeline run it enqueues a
/// chained sequence of Hangfire background jobs:
/// <code>
///   DiagnosticsAgent → FixAgent → NotifierAgent
/// </code>
/// Each step runs only after the previous job succeeds (<c>OnlyOnSucceededState</c>),
/// except NotifierAgent which uses <c>OnAnyFinishedState</c> so the team is always
/// alerted — even when an upstream agent job exhausts all its retries.
///
/// Error handling: if DiagnosticsAgent or FixAgent throws, the status is reset to
/// <see cref="PipelineRunStatus.Failed"/> and persisted before re-throwing so Hangfire
/// records the failure. NotifierAgent then fires via <c>OnAnyFinishedState</c> and
/// posts a "PIPELINE FAILED" card to Teams.
///
/// This class lives in the Agents project so both the Worker (processing server) and
/// the Api (approval endpoints) can reference it for strongly-typed job enqueueing.
/// </remarks>
public class PipelineMonitorOrchestrator(
    MonitorAgent monitorAgent,
    DiagnosticsAgent diagnosticsAgent,
    FixAgent fixAgent,
    NotifierAgent notifierAgent,
    IPipelineRunStateRepository repository,
    IBackgroundJobClient jobClient,
    ILogger<PipelineMonitorOrchestrator> logger)
{
    // ---------------------------------------------------------------------------
    // Recurring entry point — "monitor-adf-pipelines", every 2 minutes
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Polls ADF for new pipeline failures and enqueues a Diagnostics → Fix → Notify
    /// chain for each unprocessed <see cref="PipelineRunStatus.Failed"/> state.
    /// </summary>
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    [AutomaticRetry(Attempts = 0)]
    public async Task MonitorAsync(CancellationToken ct)
    {
        // Step 1: let MonitorAgent fetch ADF runs and insert any new Failed rows.
        var monitorResult = await monitorAgent.ExecuteAsync(ct);
        logger.LogInformation("MonitorAgent completed: {Message}", monitorResult.Message);

        // Step 2: queue a processing chain for every state still in Failed status.
        // MonitorAgent uses ExistsByRunIdAsync to skip already-tracked runs, so any
        // row that is still Failed at this point is either freshly inserted or is
        // recovering from a previously failed agent run.
        var failedStates = await repository.GetByStatusAsync(PipelineRunStatus.Failed, ct);

        foreach (var state in failedStates)
        {
            EnqueueDiagnosticsChain(state.Id, state.PipelineName);
        }
    }

    // ---------------------------------------------------------------------------
    // Chain step 1 — DiagnosticsAgent
    // ---------------------------------------------------------------------------

    [AutomaticRetry(Attempts = 2)]
    public async Task RunDiagnosticsAsync(Guid stateId, CancellationToken ct)
    {
        var state = await LoadOrThrowAsync(stateId, ct);

        try
        {
            var result = await diagnosticsAgent.ExecuteAsync(state, ct);

            if (!result.Success)
                logger.LogWarning(
                    "DiagnosticsAgent returned non-success for stateId={StateId}: {Message}",
                    stateId, result.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "DiagnosticsAgent threw unexpectedly for stateId={StateId}. " +
                "Resetting to Failed so the state is visible and re-triable.",
                stateId);

            state.Status = PipelineRunStatus.Failed;
            await repository.UpdateAsync(state, ct);

            // Re-throw so Hangfire records the job as failed.
            // OnAnyFinishedState on RunNotifierAsync ensures the team is still alerted.
            throw;
        }
    }

    // ---------------------------------------------------------------------------
    // Chain step 2 — FixAgent
    // ---------------------------------------------------------------------------

    [AutomaticRetry(Attempts = 2)]
    public async Task RunFixAsync(Guid stateId, CancellationToken ct)
    {
        var state = await LoadOrThrowAsync(stateId, ct);

        try
        {
            var result = await fixAgent.ExecuteAsync(state, ct);

            if (!result.Success)
                logger.LogWarning(
                    "FixAgent returned non-success for stateId={StateId}: {Message}",
                    stateId, result.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "FixAgent threw unexpectedly for stateId={StateId}. Resetting to Failed.",
                stateId);

            state.Status = PipelineRunStatus.Failed;
            await repository.UpdateAsync(state, ct);
            throw;
        }
    }

    // ---------------------------------------------------------------------------
    // Chain step 3 — NotifierAgent
    // ---------------------------------------------------------------------------

    [AutomaticRetry(Attempts = 1)]
    public async Task RunNotifierAsync(Guid stateId, CancellationToken ct)
    {
        var state = await LoadOrThrowAsync(stateId, ct);

        // Idempotency guard: if this job was enqueued twice for the same state (e.g.
        // because MonitorAsync ran again before DiagnosticsAgent changed the status),
        // skip posting a duplicate Teams card.
        if (state.TeamsMessageId is not null)
        {
            logger.LogInformation(
                "Teams card already posted for stateId={StateId} " +
                "(messageId={MessageId}). Skipping duplicate notification.",
                stateId, state.TeamsMessageId);
            return;
        }

        try
        {
            await notifierAgent.ExecuteAsync(state, ct);
        }
        catch (Exception ex)
        {
            // Notification failure must not destabilise the pipeline — log and absorb.
            logger.LogError(ex,
                "NotifierAgent threw unexpectedly for stateId={StateId}. " +
                "The Teams card may not have been sent.",
                stateId);
        }
    }

    // ---------------------------------------------------------------------------
    // Shared enqueueing helper — used by MonitorAsync and the approval endpoints
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Enqueues a Diagnostics → Fix → Notify chain for the given state.
    /// NotifierAgent uses <c>OnAnyFinishedState</c> so it fires on both success and failure.
    /// </summary>
    public void EnqueueDiagnosticsChain(Guid stateId, string pipelineName)
    {
        var diagJobId = jobClient.Enqueue<PipelineMonitorOrchestrator>(
            o => o.RunDiagnosticsAsync(stateId, CancellationToken.None));

        var fixJobId = jobClient.ContinueJobWith<PipelineMonitorOrchestrator>(
            diagJobId,
            o => o.RunFixAsync(stateId, CancellationToken.None));

        // CS4014: Hangfire serialises the expression tree; the Task returned inside
        // the lambda is never awaited in this context — that is intentional.
#pragma warning disable CS4014
        jobClient.ContinueJobWith<PipelineMonitorOrchestrator>(
            fixJobId,
            o => o.RunNotifierAsync(stateId, CancellationToken.None),
            JobContinuationOptions.OnAnyFinishedState);
#pragma warning restore CS4014

        logger.LogInformation(
            "Enqueued Diagnostics → Fix → Notify chain for " +
            "stateId={StateId} pipeline={PipelineName}.",
            stateId, pipelineName);
    }

    /// <summary>
    /// Enqueues a standalone NotifierAgent job. Used by the rejection endpoint
    /// to post a final resolution card after no remediation was taken.
    /// </summary>
    public void EnqueueNotifier(Guid stateId, string pipelineName)
    {
        jobClient.Enqueue<PipelineMonitorOrchestrator>(
            o => o.RunNotifierAsync(stateId, CancellationToken.None));

        logger.LogInformation(
            "Enqueued standalone Notify job for stateId={StateId} pipeline={PipelineName}.",
            stateId, pipelineName);
    }

    /// <summary>
    /// Enqueues a Fix → Notify chain. Used by the approval endpoint after a run is approved.
    /// </summary>
    public void EnqueueFixChain(Guid stateId, string pipelineName)
    {
        var fixJobId = jobClient.Enqueue<PipelineMonitorOrchestrator>(
            o => o.RunFixAsync(stateId, CancellationToken.None));

#pragma warning disable CS4014
        jobClient.ContinueJobWith<PipelineMonitorOrchestrator>(
            fixJobId,
            o => o.RunNotifierAsync(stateId, CancellationToken.None),
            JobContinuationOptions.OnAnyFinishedState);
#pragma warning restore CS4014

        logger.LogInformation(
            "Enqueued Fix → Notify chain (post-approval) for " +
            "stateId={StateId} pipeline={PipelineName}.",
            stateId, pipelineName);
    }

    // ---------------------------------------------------------------------------
    // Helper
    // ---------------------------------------------------------------------------

    private async Task<PipelineRunState> LoadOrThrowAsync(Guid stateId, CancellationToken ct)
    {
        var state = await repository.GetByIdAsync(stateId, ct);

        if (state is null)
            throw new InvalidOperationException(
                $"PipelineRunState {stateId} not found. " +
                "The record may have been deleted or the ID is incorrect.");

        return state;
    }
}
