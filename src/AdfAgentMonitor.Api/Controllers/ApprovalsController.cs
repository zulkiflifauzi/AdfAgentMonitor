using AdfAgentMonitor.Agents;
using AdfAgentMonitor.Api.Authentication;
using AdfAgentMonitor.Core.Enums;
using AdfAgentMonitor.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AdfAgentMonitor.Api.Controllers;

/// <summary>
/// Handles human approval decisions for pipeline remediation requests.
/// Both endpoints are invoked via the Approve / Reject buttons in the Teams Adaptive Card.
/// </summary>
[ApiController]
[Route("api/approvals")]
[ServiceFilter(typeof(ApiKeyAuthFilter))]
public class ApprovalsController(
    IPipelineRunStateRepository repository,
    ITeamsNotifierService teamsNotifier,
    PipelineMonitorOrchestrator orchestrator,
    ILogger<ApprovalsController> logger) : ControllerBase
{
    // ---------------------------------------------------------------------------
    // POST /api/approvals/{id}/approve
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Records an approval decision, re-queues FixAgent, and updates the Teams card.
    /// </summary>
    /// <remarks>
    /// State transitions: <c>PendingApproval</c> → <c>Remediating</c>.
    /// A Fix → Notify Hangfire chain is enqueued so the team sees the final outcome.
    /// The existing Teams card body is updated to reflect the approval immediately.
    /// <c>TeamsMessageId</c> is cleared so the post-fix NotifierAgent can post a fresh card.
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

        var oldMessageId = state.TeamsMessageId;

        state.ApprovalStatus = "Approved";
        state.Status         = PipelineRunStatus.Remediating;
        // Clear so RunNotifierAsync (after FixAgent completes) can post a fresh outcome
        // card rather than being skipped by the duplicate-notification idempotency guard.
        state.TeamsMessageId = null;

        await repository.UpdateAsync(state, ct);

        logger.LogInformation(
            "Approval granted for stateId={StateId} pipeline={PipelineName}.",
            id, state.PipelineName);

        // Update the existing Teams card body to show the approval immediately.
        if (oldMessageId is not null)
            await teamsNotifier.UpdateCardOutcomeAsync(oldMessageId, "approved", ct);

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
    /// "RESOLVED" card so the team knows the request was closed without action.
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

        var oldMessageId = state.TeamsMessageId;

        state.ApprovalStatus = "Rejected";
        state.Status         = PipelineRunStatus.Resolved;
        state.ResolvedAt     = DateTimeOffset.UtcNow;
        // Clear so NotifierAgent can post a fresh "RESOLVED" card informing the team
        // of the rejection outcome.
        state.TeamsMessageId = null;

        await repository.UpdateAsync(state, ct);

        logger.LogInformation(
            "Approval rejected for stateId={StateId} pipeline={PipelineName}.",
            id, state.PipelineName);

        // Update the existing Teams card body to show the rejection immediately.
        if (oldMessageId is not null)
            await teamsNotifier.UpdateCardOutcomeAsync(oldMessageId, "rejected", ct);

        // Enqueue NotifierAgent to post a new resolution card informing the team.
        orchestrator.EnqueueNotifier(state.Id, state.PipelineName);

        return Ok(new
        {
            message = "Rejection recorded. No automated remediation will be attempted.",
            stateId = id
        });
    }
}
