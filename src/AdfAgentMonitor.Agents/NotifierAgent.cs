using AdfAgentMonitor.Core.Entities;
using AdfAgentMonitor.Core.Interfaces;
using AdfAgentMonitor.Core.Models;
using Microsoft.Extensions.Logging;

namespace AdfAgentMonitor.Agents;

/// <summary>
/// Posts a Teams Adaptive Card for any pipeline run state.
/// <para>
/// When <see cref="Core.Enums.PipelineRunStatus.PendingApproval"/> the card includes
/// Approve and Reject action buttons. All other statuses produce an informational card.
/// The Graph message ID is stored in <see cref="PipelineRunState.TeamsMessageId"/>
/// so the card can be updated when an approval decision arrives.
/// </para>
/// </summary>
/// <remarks>
/// This agent does not transition <see cref="PipelineRunState.Status"/>; the caller is
/// responsible for invoking it after FixAgent or DiagnosticsAgent have persisted their
/// status changes.
/// </remarks>
public class NotifierAgent(
    ITeamsNotifierService notifierService,
    IPipelineRunStateRepository repository,
    ILogger<NotifierAgent> logger) : IAgent
{
    // ---------------------------------------------------------------------------
    // IAgent
    // ---------------------------------------------------------------------------

    public async Task<AgentResult> ExecuteAsync(PipelineRunState state, CancellationToken ct)
    {
        logger.LogInformation(
            "NotifierAgent posting card for pipeline {PipelineName} (runId={RunId}, status={Status}).",
            state.PipelineName, state.PipelineRunId, state.Status);

        var messageId = await notifierService.SendNotificationAsync(state, ct);

        if (messageId is null)
        {
            logger.LogWarning(
                "NotifierAgent could not post Teams card for runId={RunId}. " +
                "Continuing without a message ID.",
                state.PipelineRunId);

            return new AgentResult(
                Success:    false,
                Message:    "Teams card post failed — see logs for details.",
                NextStatus: state.Status);
        }

        // Persist the Graph message ID so approval updates can target the card.
        state.TeamsMessageId = messageId;
        await repository.UpdateAsync(state, ct);

        logger.LogInformation(
            "NotifierAgent stored TeamsMessageId={MessageId} for runId={RunId}.",
            messageId, state.PipelineRunId);

        return new AgentResult(
            Success:    true,
            Message:    $"Teams card posted (messageId={messageId}).",
            NextStatus: state.Status);
    }
}
