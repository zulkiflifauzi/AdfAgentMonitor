using AdfAgentMonitor.Core.Entities;

namespace AdfAgentMonitor.Core.Interfaces;

public interface ITeamsNotifierService
{
    /// <summary>
    /// Posts a notification card to the configured Teams channel for the given run state.
    /// <para>
    /// When <see cref="PipelineRunState.Status"/> is <c>PendingApproval</c> the card
    /// includes Approve and Reject action buttons. All other statuses produce an
    /// informational card with no actions.
    /// </para>
    /// Returns the Graph message ID of the posted card, or <see langword="null"/> when
    /// the Graph API call fails (the caller should not throw).
    /// </summary>
    Task<string?> SendNotificationAsync(PipelineRunState state, CancellationToken ct = default);

    /// <summary>
    /// Updates the body of a previously posted card message to reflect the final outcome
    /// after the approval decision is received.
    /// </summary>
    Task UpdateCardOutcomeAsync(string messageId, string outcome, CancellationToken ct = default);
}
