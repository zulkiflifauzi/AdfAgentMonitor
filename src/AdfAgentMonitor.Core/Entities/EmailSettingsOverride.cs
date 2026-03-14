namespace AdfAgentMonitor.Core.Entities;

/// <summary>
/// Single-row table (Id always = 1) storing operator-supplied overrides for SMTP settings.
/// Any null field means "use the value from appsettings.json / environment variable".
/// </summary>
public class EmailSettingsOverride
{
    public int     Id               { get; set; } = 1;
    public string? SmtpHost         { get; set; }
    public int?    SmtpPort         { get; set; }
    public bool?   UseSsl           { get; set; }
    public string? Username         { get; set; }
    public string? Password         { get; set; }
    public string? FromAddress      { get; set; }
    public string? FromName         { get; set; }
    public string? DashboardBaseUrl { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
