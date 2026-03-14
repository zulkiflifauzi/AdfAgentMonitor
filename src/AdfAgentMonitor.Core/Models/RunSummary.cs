namespace AdfAgentMonitor.Core.Models;

/// <summary>
/// Aggregated counts returned by <c>GET /api/runs/summary</c> for the dashboard stat cards.
/// All "today" figures are relative to midnight UTC of the current calendar day.
/// </summary>
public record RunSummary(
    int TotalToday,
    int FailedToday,
    int RemediatedToday,
    int PendingApproval);
