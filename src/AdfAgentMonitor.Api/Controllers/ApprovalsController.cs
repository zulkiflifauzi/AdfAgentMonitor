using AdfAgentMonitor.Agents;
using AdfAgentMonitor.Api.Authentication;
using AdfAgentMonitor.Core.Enums;
using AdfAgentMonitor.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AdfAgentMonitor.Api.Controllers;

/// <summary>
/// Handles human approval decisions for pipeline remediation requests.
/// Both endpoints are invoked via the Approve / Reject buttons in the Dashboard.
/// </summary>
[ApiController]
[Route("api/approvals")]
[ServiceFilter(typeof(ApiKeyAuthFilter))]
public class ApprovalsController(
    IPipelineRunStateRepository repository,
    IEmailNotifierService emailNotifier,
    PipelineMonitorOrchestrator orchestrator,
    ILogger<ApprovalsController> logger) : ControllerBase
{
    // ---------------------------------------------------------------------------
    // POST /api/approvals/{id}/approve
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Records an approval decision, re-queues FixAgent, and sends an outcome email.
    /// </summary>
    /// <remarks>
    /// State transitions: <c>PendingApproval</c> → <c>Remediating</c>.
    /// A Fix → Notify Hangfire chain is enqueued so the team sees the final outcome.
    /// An outcome email is sent immediately to reflect the approval decision.
    /// </remarks>
    [HttpPost("{id:guid}/approve")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ApproveAsync(Guid id, CancellationToken ct)
    {
        var state = await repository.GetByIdAsync(id, ct);

        if (state is null)
            return NotFound(new { error = $"PipelineRunState {id} not found." });

        if (state.Status != PipelineRunStatus.PendingApproval)
            return Conflict(new
            {
                error = $"Run is in status '{state.Status}', not PendingApproval. " +
                        "No action taken."
            });

        state.ApprovalStatus = "Approved";
        state.Status         = PipelineRunStatus.Remediating;

        await repository.UpdateAsync(state, ct);

        logger.LogInformation(
            "Approval granted for stateId={StateId} pipeline={PipelineName}.",
            id, state.PipelineName);

        // Send an outcome email to notify the recipient of the approval.
        await emailNotifier.SendOutcomeEmailAsync(state, "approved", ct);

        // Enqueue Fix → Notify so remediation proceeds and the team sees the final result.
        orchestrator.EnqueueFixChain(state.Id, state.PipelineName);

        return Ok(new
        {
            message = "Approval recorded. FixAgent has been queued.",
            stateId = id
        });
    }

    // ---------------------------------------------------------------------------
    // POST /api/approvals/{id}/reject
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Records a rejection decision, marks the run as resolved, and notifies the team.
    /// </summary>
    /// <remarks>
    /// State transitions: <c>PendingApproval</c> → <c>Resolved</c>.
    /// No remediation is attempted. NotifierAgent is enqueued to post a final
    /// "RESOLVED" email so the team knows the request was closed without action.
    /// </remarks>
    [HttpPost("{id:guid}/reject")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RejectAsync(Guid id, CancellationToken ct)
    {
        var state = await repository.GetByIdAsync(id, ct);

        if (state is null)
            return NotFound(new { error = $"PipelineRunState {id} not found." });

        if (state.Status != PipelineRunStatus.PendingApproval)
            return Conflict(new
            {
                error = $"Run is in status '{state.Status}', not PendingApproval. " +
                        "No action taken."
            });

        state.ApprovalStatus = "Rejected";
        state.Status         = PipelineRunStatus.Resolved;
        state.ResolvedAt     = DateTimeOffset.UtcNow;

        await repository.UpdateAsync(state, ct);

        logger.LogInformation(
            "Approval rejected for stateId={StateId} pipeline={PipelineName}.",
            id, state.PipelineName);

        // Send an outcome email to notify the recipient of the rejection.
        await emailNotifier.SendOutcomeEmailAsync(state, "rejected", ct);

        // Enqueue NotifierAgent to send a resolution email informing the team.
        orchestrator.EnqueueNotifier(state.Id, state.PipelineName);

        return Ok(new
        {
            message = "Rejection recorded. No automated remediation will be attempted.",
            stateId = id
        });
    }
}
