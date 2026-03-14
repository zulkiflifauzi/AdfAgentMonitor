namespace AdfAgentMonitor.Infrastructure.Settings;

/// <summary>
/// Bound from configuration section "Hangfire".
/// </summary>
public class HangfireSettings
{
    public const string SectionName = "Hangfire";

    /// <summary>
    /// SQL Server connection string for Hangfire's job storage schema.
    /// When empty the host falls back to <c>ConnectionStrings:DefaultConnection</c>
    /// so a single connection string is sufficient for local development.
    /// In production use a dedicated connection string to control permissions.
    /// Supply via <c>HANGFIRE__SQLCONNECTIONSTRING</c> environment variable.
    /// </summary>
    public string SqlConnectionString { get; init; } = string.Empty;

    /// <summary>Number of Hangfire background worker threads. Default: 5.</summary>
    public int WorkerCount { get; init; } = 5;
}
