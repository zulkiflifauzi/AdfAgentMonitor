using AdfAgentMonitor.Core.Entities;

namespace AdfAgentMonitor.Core.Interfaces;

public interface IAdfService
{
    /// <summary>
    /// Returns stubs for all pipeline runs that ended with status "Failed" within the
    /// configured lookback window. The returned objects are not persisted.
    /// Returns an empty collection (never throws) when the ADF API is unreachable.
    /// </summary>
    Task<IEnumerable<PipelineRunState>> GetFailedRunsAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns a formatted log string of all activity runs for the given pipeline run ID.
    /// Returns an empty string when no data is available or the ADF API is unreachable.
    /// </summary>
    Task<string> GetActivityRunLogsAsync(string pipelineRunId, CancellationToken ct = default);

    /// <summary>
    /// Triggers a new run of <paramref name="pipelineName"/> starting from the last failed
    /// activity of <paramref name="originalRunId"/> (ADF recovery run).
    /// Returns the new run ID on success, or <see langword="null"/> when the API call fails.
    /// </summary>
    Task<string?> TriggerPipelineRerunAsync(
        string pipelineName,
        string originalRunId,
        CancellationToken ct = default);

    /// <summary>
    /// Initiates a start of the configured default Integration Runtime.
    /// Uses <c>WaitUntil.Started</c> — the method returns as soon as the operation is accepted,
    /// without blocking until the IR is fully online.
    /// Returns <see langword="false"/> when no IR name is configured or the API call fails.
    /// </summary>
    Task<bool> RestartIntegrationRuntimeAsync(CancellationToken ct = default);
}
