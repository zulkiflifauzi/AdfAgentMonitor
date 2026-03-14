using AdfAgentMonitor.Core.Entities;
using AdfAgentMonitor.Core.Enums;
using AdfAgentMonitor.Core.Models;

namespace AdfAgentMonitor.Core.Interfaces;

public interface IPipelineRunStateRepository
{
    /// <summary>Returns the row with the given primary key, or <see langword="null"/> if not found.</summary>
    Task<PipelineRunState?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Returns true if a row with the given ADF run ID already exists.</summary>
    Task<bool> ExistsByRunIdAsync(string pipelineRunId, CancellationToken ct = default);

    /// <summary>Persists a new state row. Throws if the PipelineRunId is not unique.</summary>
    Task AddAsync(PipelineRunState state, CancellationToken ct = default);

    /// <summary>
    /// Returns all rows whose <see cref="PipelineRunState.Status"/> matches the given value.
    /// Used by agents to find work items in their target status.
    /// </summary>
    Task<IReadOnlyList<PipelineRunState>> GetByStatusAsync(PipelineRunStatus status, CancellationToken ct = default);

    /// <summary>
    /// Persists changes to an existing row and updates <see cref="PipelineRunState.UpdatedAt"/>.
    /// Throws <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/> if
    /// another agent has already modified the same row (optimistic concurrency via UpdatedAt).
    /// </summary>
    Task UpdateAsync(PipelineRunState state, CancellationToken ct = default);

    /// <summary>
    /// Returns a page of <see cref="PipelineRunState"/> rows, ordered newest-first,
    /// with optional filters on status, risk, pipeline name, and creation date range.
    /// </summary>
    Task<(IReadOnlyList<PipelineRunState> Items, int TotalCount)> GetPagedAsync(
        PipelineRunStatus? status   = null,
        RemediationRisk?   risk     = null,
        string?            name     = null,
        DateTimeOffset?    fromDate = null,
        DateTimeOffset?    toDate   = null,
        int                page     = 1,
        int                pageSize = 50,
        CancellationToken  ct       = default);

    /// <summary>
    /// Returns aggregated counts for the dashboard stat cards.
    /// All "today" figures are computed relative to midnight UTC of the current day.
    /// </summary>
    Task<RunSummary> GetSummaryAsync(CancellationToken ct = default);
}
