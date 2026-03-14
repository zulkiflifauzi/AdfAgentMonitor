namespace AdfAgentMonitor.Dashboard.Services;

public class ApiClientSettings
{
    public string ApiBaseUrl { get; init; } = string.Empty;

    /// <remarks>
    /// Stored in wwwroot/appsettings.json and therefore visible to anyone who loads the app.
    /// Treat this as a low-privilege coordination token, not a high-security secret.
    /// For production, consider proxying API calls through a server-side BFF instead.
    /// </remarks>
    public string ApiKey { get; init; } = string.Empty;
}
