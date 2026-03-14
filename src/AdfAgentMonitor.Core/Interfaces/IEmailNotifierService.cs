using AdfAgentMonitor.Core.Entities;

namespace AdfAgentMonitor.Core.Interfaces;

public interface IEmailNotifierService
{
    /// <summary>
    /// Sends an HTML notification email for the given run state.
    /// Returns true on success, false if sending failed (never throws).
    /// </summary>
    Task<bool> SendNotificationAsync(PipelineRunState state, CancellationToken ct = default);

    /// <summary>
    /// Sends a follow-up outcome email after an approval decision.
    /// </summary>
    Task SendOutcomeEmailAsync(PipelineRunState state, string outcome, CancellationToken ct = default);
}
