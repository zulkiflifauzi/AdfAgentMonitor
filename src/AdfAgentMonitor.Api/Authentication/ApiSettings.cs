namespace AdfAgentMonitor.Api.Authentication;

/// <summary>
/// Bound from the "Api" configuration section.
/// The API key must be supplied via environment variable or Azure Key Vault — never appsettings.json.
/// </summary>
public class ApiSettings
{
    public const string SectionName = "Api";

    /// <summary>
    /// Secret value expected in the <c>X-Api-Key</c> request header.
    /// Inject via <c>API__APIKEY</c> environment variable.
    /// </summary>
    public string ApiKey { get; init; } = string.Empty;
}
