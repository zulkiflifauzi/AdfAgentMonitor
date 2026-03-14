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

    /// <summary>
    /// Sends a test email to <paramref name="recipientEmail"/> using the current effective
    /// settings (appsettings + any DB overrides). Returns a result message describing
    /// success or the failure reason. Never throws.
    /// </summary>
    Task<(bool Success, string Message)> SendTestEmailAsync(string recipientEmail, CancellationToken ct = default);
}
