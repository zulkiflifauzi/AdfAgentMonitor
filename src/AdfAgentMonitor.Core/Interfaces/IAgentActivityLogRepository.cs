using AdfAgentMonitor.Core.Entities;

namespace AdfAgentMonitor.Core.Interfaces;

public interface IAgentActivityLogRepository
{
    /// <summary>Appends a new activity log row. Never throws on success.</summary>
    Task AddAsync(AgentActivityLog log, CancellationToken ct = default);

    /// <summary>
    /// Returns a page of activity log entries in descending timestamp order,
    /// optionally filtered by agent name, success flag, and date range.
    /// </summary>
    Task<(IReadOnlyList<AgentActivityLog> Items, int TotalCount)> GetPagedAsync(
        string?         agentName = null,
        bool?           success   = null,
        DateTimeOffset? from      = null,
        DateTimeOffset? to        = null,
        int             page      = 1,
        int             pageSize  = 50,
        CancellationToken ct      = default);
}
