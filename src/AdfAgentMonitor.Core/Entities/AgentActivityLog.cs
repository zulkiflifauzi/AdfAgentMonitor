namespace AdfAgentMonitor.Core.Entities;

/// <summary>
/// Immutable audit record written by each agent after every execution.
/// Drives the Agent Activity timeline in the dashboard.
/// </summary>
public class AgentActivityLog
{
    public Guid Id { get; set; }

    /// <summary>Canonical agent name: MonitorAgent, DiagnosticsAgent, FixAgent, NotifierAgent.</summary>
    public string AgentName { get; set; } = string.Empty;

    /// <summary>
    /// Foreign key to <see cref="PipelineRunState.Id"/>.
    /// Null for MonitorAgent entries that did not produce a new state row
    /// (e.g. "no failed runs found").
    /// </summary>
    public Guid? PipelineRunId { get; set; }

    /// <summary>
    /// Denormalised pipeline name — populated even if <see cref="PipelineRunId"/> is null
    /// so the timeline can display a label without a join.
    /// Empty string for agent sweeps that found no work.
    /// </summary>
    public string PipelineName { get; set; } = string.Empty;

    /// <summary>Short description of what the agent did (e.g. "Diagnosed pipeline failure").</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Human-readable outcome or error message from the agent.</summary>
    public string? ResultMessage { get; set; }

    /// <summary><c>true</c> when the agent completed its task without error.</summary>
    public bool Success { get; set; }

    public DateTimeOffset Timestamp { get; set; }
}
