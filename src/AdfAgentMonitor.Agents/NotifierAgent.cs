using AdfAgentMonitor.Core.Entities;
using AdfAgentMonitor.Core.Interfaces;
using AdfAgentMonitor.Core.Models;
using Microsoft.Extensions.Logging;

namespace AdfAgentMonitor.Agents;

/// <summary>
/// Sends an HTML email notification for any pipeline run state.
/// <para>
/// When <see cref="Core.Enums.PipelineRunStatus.PendingApproval"/> the email includes
/// a link to the Approvals Dashboard. All other statuses produce an informational email.
/// </para>
/// </summary>
/// <remarks>
/// This agent does not transition <see cref="PipelineRunState.Status"/>; the caller is
/// responsible for invoking it after FixAgent or DiagnosticsAgent have persisted their
/// status changes.
/// </remarks>
public class NotifierAgent(
    IEmailNotifierService notifierService,
    IAgentActivityLogRepository activityLog,
    ILogger<NotifierAgent> logger) : IAgent
{
    // ---------------------------------------------------------------------------
    // IAgent
    // ---------------------------------------------------------------------------

    public async Task<AgentResult> ExecuteAsync(PipelineRunState state, CancellationToken ct)
    {
        logger.LogInformation(
            "NotifierAgent sending email for pipeline {PipelineName} (runId={RunId}, status={Status}).",
            state.PipelineName, state.PipelineRunId, state.Status);

        var success = await notifierService.SendNotificationAsync(state, ct);

        if (!success)
        {
            logger.LogWarning(
                "NotifierAgent could not send email for runId={RunId}.",
                state.PipelineRunId);

            return new AgentResult(
                Success:    false,
                Message:    "Email send failed — see logs.",
                NextStatus: state.Status);
        }

        try
        {
            await activityLog.AddAsync(new AgentActivityLog
            {
                Id            = Guid.NewGuid(),
                AgentName     = "NotifierAgent",
                PipelineRunId = state.Id,
                PipelineName  = state.PipelineName,
                Action        = "Sent email notification",
                ResultMessage = $"Email sent to configured recipient (status={state.Status}).",
                Success       = true,
                Timestamp     = DateTimeOffset.UtcNow,
            }, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "NotifierAgent failed to write activity log for runId={RunId}.", state.PipelineRunId);
        }

        return new AgentResult(
            Success:    true,
            Message:    "Email notification sent.",
            NextStatus: state.Status);
    }
}
