namespace AdfAgentMonitor.Dashboard.Services;

/// <summary>
/// Holds runtime-mutable API connection overrides loaded from localStorage.
/// Injected into <see cref="SettingsOverridingHandler"/> so that changes saved
/// by the Settings page take effect immediately (current session) and are also
/// persisted to localStorage so they survive a page reload.
/// </summary>
public sealed class DashboardSettingsService
{
    /// <summary>
    /// When non-null, replaces the HttpClient base address for every outgoing request.
    /// </summary>
    public string? ApiBaseUrl { get; set; }

    /// <summary>
    /// When non-null, replaces the <c>X-Api-Key</c> header for every outgoing request.
    /// </summary>
    public string? ApiKey { get; set; }

    public bool HasBaseUrlOverride => !string.IsNullOrWhiteSpace(ApiBaseUrl);
    public bool HasApiKeyOverride   => !string.IsNullOrWhiteSpace(ApiKey);
}
