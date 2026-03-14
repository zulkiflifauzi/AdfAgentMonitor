using AdfAgentMonitor.Core.Enums;

namespace AdfAgentMonitor.Core.Entities;

public class PipelineRunState
{
    public Guid Id { get; set; }

    /// <summary>The run ID assigned by Azure Data Factory.</summary>
    public string PipelineRunId { get; set; } = string.Empty;

    public string PipelineName { get; set; } = string.Empty;

    public string FactoryName { get; set; } = string.Empty;

    public PipelineRunStatus Status { get; set; }

    public DateTimeOffset? FailedAt { get; set; }

    /// <summary>Set by DiagnosticsAgent after root-cause analysis.</summary>
    public DiagnosisCode? DiagnosisCode { get; set; }

    /// <summary>Human-readable diagnosis narrative produced by the DiagnosticsAgent.</summary>
    public string? DiagnosisSummary { get; set; }

    /// <summary>JSON-serialised remediation steps produced by the FixAgent planner.</summary>
    public string? RemediationPlan { get; set; }

    /// <summary>Risk level of the proposed remediation; determines whether human approval is required.</summary>
    public RemediationRisk? RemediationRisk { get; set; }

    /// <summary>Tracks the Teams Adaptive Card approval state: Pending, Approved, or Rejected.</summary>
    public string? ApprovalStatus { get; set; }

    /// <summary>
    /// Graph message ID returned by the Teams channel after NotifierAgent posts a card.
    /// Stored so the card can be updated when the approval decision arrives.
    /// </summary>
    public string? TeamsMessageId { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
