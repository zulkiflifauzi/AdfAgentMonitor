namespace AdfAgentMonitor.Infrastructure.Settings;

/// <summary>
/// Bound from configuration section "AzureDataFactory".
/// </summary>
public class AdfSettings
{
    public const string SectionName = "AzureDataFactory";

    // ---------------------------------------------------------------------------
    // Azure resource identifiers
    // ---------------------------------------------------------------------------

    public string SubscriptionId { get; init; } = string.Empty;
    public string ResourceGroup  { get; init; } = string.Empty;
    public string FactoryName    { get; init; } = string.Empty;

    // ---------------------------------------------------------------------------
    // Service Principal credentials (optional)
    // When all three are set, ClientSecretCredential is used instead of
    // DefaultAzureCredential. Leave empty in production to rely on Managed Identity.
    // ---------------------------------------------------------------------------

    /// <summary>Azure AD tenant (directory) ID.</summary>
    public string TenantId { get; init; } = string.Empty;

    /// <summary>Service principal application (client) ID.</summary>
    public string ClientId { get; init; } = string.Empty;

    /// <summary>
    /// Service principal client secret.
    /// Must be supplied via environment variable or Key Vault — never committed to source control.
    /// </summary>
    public string ClientSecret { get; init; } = string.Empty;

    // ---------------------------------------------------------------------------
    // Polling behaviour
    // ---------------------------------------------------------------------------

    /// <summary>How far back to scan for failed runs on each MonitorAgent execution.</summary>
    public int LookbackMinutes { get; init; } = 60;

    /// <summary>
    /// Name of the Integration Runtime to restart when <c>IROffline</c> is diagnosed.
    /// Required for FixAgent's IR remediation path; leave empty to always escalate IR failures.
    /// </summary>
    public string DefaultIntegrationRuntimeName { get; init; } = string.Empty;

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Returns <see langword="true"/> when all three SP credential fields are populated,
    /// indicating that <see cref="Azure.Identity.ClientSecretCredential"/> should be used.
    /// </summary>
    public bool HasServicePrincipalCredentials =>
        !string.IsNullOrEmpty(TenantId) &&
        !string.IsNullOrEmpty(ClientId) &&
        !string.IsNullOrEmpty(ClientSecret);
}
