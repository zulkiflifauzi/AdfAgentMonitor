using AdfAgentMonitor.Core.Entities;
using AdfAgentMonitor.Core.Enums;
using AdfAgentMonitor.Core.Interfaces;
using AdfAgentMonitor.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace AdfAgentMonitor.Infrastructure.Persistence;

public class PipelineRunStateRepository(AppDbContext db) : IPipelineRunStateRepository
{
    /// <inheritdoc/>
    public Task<PipelineRunState?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => db.PipelineRunStates.FirstOrDefaultAsync(s => s.Id == id, ct);

    /// <inheritdoc/>
    public Task<bool> ExistsByRunIdAsync(string pipelineRunId, CancellationToken ct = default)
        => db.PipelineRunStates
             .AnyAsync(r => r.PipelineRunId == pipelineRunId, ct);

    /// <inheritdoc/>
    public async Task AddAsync(PipelineRunState state, CancellationToken ct = default)
    {
        db.PipelineRunStates.Add(state);
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PipelineRunState>> GetByStatusAsync(
        PipelineRunStatus status,
        CancellationToken ct = default)
        => await db.PipelineRunStates
                   .Where(r => r.Status == status)
                   .OrderBy(r => r.CreatedAt)
                   .ToListAsync(ct);

    /// <inheritdoc/>
    public async Task UpdateAsync(PipelineRunState state, CancellationToken ct = default)
    {
        state.UpdatedAt = DateTimeOffset.UtcNow;
        db.PipelineRunStates.Update(state);
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<(IReadOnlyList<PipelineRunState> Items, int TotalCount)> GetPagedAsync(
        PipelineRunStatus? status   = null,
        RemediationRisk?   risk     = null,
        string?            name     = null,
        DateTimeOffset?    fromDate = null,
        DateTimeOffset?    toDate   = null,
        int                page     = 1,
        int                pageSize = 50,
        CancellationToken  ct       = default)
    {
        var query = db.PipelineRunStates.AsQueryable();

        if (status.HasValue)
            query = query.Where(r => r.Status == status.Value);

        if (risk.HasValue)
            query = query.Where(r => r.RemediationRisk == risk.Value);

        if (!string.IsNullOrWhiteSpace(name))
            query = query.Where(r => r.PipelineName.Contains(name));

        if (fromDate.HasValue)
            query = query.Where(r => r.CreatedAt >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(r => r.CreatedAt <= toDate.Value);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    /// <inheritdoc/>
    public async Task<RunSummary> GetSummaryAsync(CancellationToken ct = default)
    {
        // All "today" windows are anchored to midnight UTC of the current calendar day.
        var todayStart = new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero);

        var totalToday = await db.PipelineRunStates
            .CountAsync(r => r.CreatedAt >= todayStart, ct);

        var failedToday = await db.PipelineRunStates
            .CountAsync(r => r.FailedAt >= todayStart, ct);

        var remediatedToday = await db.PipelineRunStates
            .CountAsync(r => r.Status == PipelineRunStatus.Resolved
                          && r.ResolvedAt >= todayStart, ct);

        var pendingApproval = await db.PipelineRunStates
            .CountAsync(r => r.Status == PipelineRunStatus.PendingApproval, ct);

        return new RunSummary(totalToday, failedToday, remediatedToday, pendingApproval);
    }
}
