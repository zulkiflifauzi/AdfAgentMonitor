using AdfAgentMonitor.Core.Entities;
using AdfAgentMonitor.Core.Enums;
using AdfAgentMonitor.Core.Interfaces;
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
}
