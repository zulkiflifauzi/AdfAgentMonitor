using AdfAgentMonitor.Core.Entities;
using AdfAgentMonitor.Core.Models;

namespace AdfAgentMonitor.Dashboard.Services;

/// <summary>Page of agent activity log entries returned by GET /api/activity.</summary>
public record ActivityPage(
    List<AgentActivityLog> Items,
    int TotalCount,
    int Page,
    int PageSize);

public interface IMonitorApiClient
{
    /// <summary>Returns aggregated counts for the dashboard stat cards.</summary>
    Task<RunSummary> GetSummaryAsync(CancellationToken ct = default);

    Task<List<PipelineRunState>> GetAllRunsAsync(CancellationToken ct = default);
    Task<List<PipelineRunState>> GetFilteredRunsAsync(RunFilter filter, CancellationToken ct = default);
    Task<List<PipelineRunState>> GetPendingApprovalsAsync(CancellationToken ct = default);
    Task<PipelineRunState> GetRunByIdAsync(Guid id, CancellationToken ct = default);
    Task ApproveAsync(Guid id, CancellationToken ct = default);
    Task RejectAsync(Guid id, string reason, CancellationToken ct = default);

    /// <summary>
    /// Fetches raw ADF activity log text for a run.
    /// Returns <c>null</c> when no log data exists yet or the endpoint returns 404.
    /// </summary>
    Task<string?> GetRunLogsAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Returns a paged slice of agent activity log entries from GET /api/activity.
    /// </summary>
    Task<ActivityPage> GetActivityAsync(
        string?         agentName = null,
        bool?           success   = null,
        DateTimeOffset? from      = null,
        DateTimeOffset? to        = null,
        int             page      = 1,
        int             pageSize  = 50,
        CancellationToken ct      = default);

    /// <summary>
    /// Calls <c>GET /api/health</c> to verify API reachability and key validity.
    /// Never throws — returns a result record instead.
    /// </summary>
    Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct = default);
}

/// <summary>Result of a Test Connection call.</summary>
public record ConnectionTestResult(bool Success, string Message);
