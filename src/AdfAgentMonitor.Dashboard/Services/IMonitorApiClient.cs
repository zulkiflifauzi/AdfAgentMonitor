using AdfAgentMonitor.Core.Entities;
using AdfAgentMonitor.Core.Models;

namespace AdfAgentMonitor.Dashboard.Services;

/// <summary>Page of agent activity log entries returned by GET /api/activity.</summary>
public record ActivityPage(
    List<AgentActivityLog> Items,
    int TotalCount,
    int Page,
    int PageSize);

public interface IMonitorApiClient
{
    /// <summary>Returns aggregated counts for the dashboard stat cards.</summary>
    Task<RunSummary> GetSummaryAsync(CancellationToken ct = default);

    Task<List<PipelineRunState>> GetAllRunsAsync(CancellationToken ct = default);
    Task<List<PipelineRunState>> GetFilteredRunsAsync(RunFilter filter, CancellationToken ct = default);
    Task<List<PipelineRunState>> GetPendingApprovalsAsync(CancellationToken ct = default);
    Task<PipelineRunState> GetRunByIdAsync(Guid id, CancellationToken ct = default);
    Task ApproveAsync(Guid id, CancellationToken ct = default);
    Task RejectAsync(Guid id, string reason, CancellationToken ct = default);

    /// <summary>
    /// Fetches raw ADF activity log text for a run.
    /// Returns <c>null</c> when no log data exists yet or the endpoint returns 404.
    /// </summary>
    Task<string?> GetRunLogsAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Returns a paged slice of agent activity log entries from GET /api/activity.
    /// </summary>
    Task<ActivityPage> GetActivityAsync(
        string?         agentName = null,
        bool?           success   = null,
        DateTimeOffset? from      = null,
        DateTimeOffset? to        = null,
        int             page      = 1,
        int             pageSize  = 50,
        CancellationToken ct      = default);

    /// <summary>
    /// Calls <c>GET /api/health</c> to verify API reachability and key validity.
    /// Never throws — returns a result record instead.
    /// </summary>
    Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns the currently configured notification recipient email addresses.
    /// </summary>
    Task<List<string>> GetNotificationRecipientsAsync(CancellationToken ct = default);

    /// <summary>
    /// Saves the notification recipient email addresses via <c>PUT /api/settings/notifications</c>.
    /// </summary>
    Task SetNotificationRecipientsAsync(List<string> emails, CancellationToken ct = default);

    /// <summary>
    /// Returns the effective email (SMTP) settings — appsettings values overlaid with any DB overrides.
    /// Password is never returned; <c>HasPassword</c> indicates whether one is stored.
    /// </summary>
    Task<EmailSettingsDto?> GetEmailSettingsAsync(CancellationToken ct = default);

    /// <summary>
    /// Saves email settings overrides via <c>PUT /api/settings/email</c>.
    /// Pass <c>null</c> for any field to remove that override (revert to appsettings).
    /// Pass <c>null</c> for <c>Password</c> to leave the stored password unchanged.
    /// </summary>
    Task<EmailSettingsDto?> SetEmailSettingsAsync(EmailSettingsRequest request, CancellationToken ct = default);

    /// <summary>
    /// Clears all email settings overrides via <c>DELETE /api/settings/email</c>.
    /// </summary>
    Task ClearEmailSettingsAsync(CancellationToken ct = default);
}

/// <summary>Result of a Test Connection call.</summary>
public record ConnectionTestResult(bool Success, string Message);

/// <summary>Effective email settings returned by GET /api/settings/email.</summary>
public record EmailSettingsDto(
    string SmtpHost,
    int    SmtpPort,
    bool   UseSsl,
    string Username,
    bool   HasPassword,
    string FromAddress,
    string FromName,
    string DashboardBaseUrl,
    bool   HasOverrides);

/// <summary>Payload for PUT /api/settings/email. Null fields remove the override for that setting.</summary>
public record EmailSettingsRequest(
    string? SmtpHost,
    int?    SmtpPort,
    bool?   UseSsl,
    string? Username,
    string? Password,
    string? FromAddress,
    string? FromName,
    string? DashboardBaseUrl);
