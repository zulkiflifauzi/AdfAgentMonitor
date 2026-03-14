namespace AdfAgentMonitor.Infrastructure.Settings;

public class EmailSettings
{
    public const string SectionName = "Email";

    public string SmtpHost     { get; set; } = string.Empty;
    public int    SmtpPort     { get; set; } = 587;
    public bool   UseSsl       { get; set; } = true;
    public string Username     { get; set; } = string.Empty;
    public string Password     { get; set; } = string.Empty;
    public string FromAddress  { get; set; } = string.Empty;
    public string FromName     { get; set; } = "ADF Agent Monitor";
    /// <summary>Base URL of the Dashboard (e.g. https://localhost:7071). Used to build approval links in emails.</summary>
    public string DashboardBaseUrl { get; set; } = string.Empty;
}
