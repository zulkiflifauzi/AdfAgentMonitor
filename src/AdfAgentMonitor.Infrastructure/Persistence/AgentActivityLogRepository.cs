using AdfAgentMonitor.Core.Entities;
using AdfAgentMonitor.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AdfAgentMonitor.Infrastructure.Persistence;

public class AgentActivityLogRepository(AppDbContext db) : IAgentActivityLogRepository
{
    /// <inheritdoc/>
    public async Task AddAsync(AgentActivityLog log, CancellationToken ct = default)
    {
        db.AgentActivityLogs.Add(log);
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<(IReadOnlyList<AgentActivityLog> Items, int TotalCount)> GetPagedAsync(
        string?         agentName = null,
        bool?           success   = null,
        DateTimeOffset? from      = null,
        DateTimeOffset? to        = null,
        int             page      = 1,
        int             pageSize  = 50,
        CancellationToken ct      = default)
    {
        var query = db.AgentActivityLogs.AsQueryable();

        if (!string.IsNullOrWhiteSpace(agentName))
            query = query.Where(e => e.AgentName == agentName);

        if (success.HasValue)
            query = query.Where(e => e.Success == success.Value);

        if (from.HasValue)
            query = query.Where(e => e.Timestamp >= from.Value);

        if (to.HasValue)
            query = query.Where(e => e.Timestamp <= to.Value);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(e => e.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }
}
