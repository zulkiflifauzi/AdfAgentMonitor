namespace AdfAgentMonitor.Infrastructure.Settings;

/// <summary>
/// Bound from configuration section "Teams".
/// All values are required — startup will fail fast if any are missing.
/// </summary>
public class TeamsSettings
{
    public const string SectionName = "Teams";

    /// <summary>The Azure AD tenant-level Teams team ID (GUID).</summary>
    public string TeamId { get; init; } = string.Empty;

    /// <summary>The channel ID within that team where approval cards are posted.</summary>
    public string ChannelId { get; init; } = string.Empty;

    /// <summary>
    /// Base URL of the AdfAgentMonitor.Api instance, e.g. https://adfmonitor.example.com.
    /// Used to build the Approve / Reject action URLs embedded in the Adaptive Card.
    /// </summary>
    public string ApprovalWebhookBaseUrl { get; init; } = string.Empty;
}
