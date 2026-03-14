namespace AdfAgentMonitor.Core.Entities;

public class NotificationSettings
{
    public int    Id             { get; set; } = 1; // always 1
    public string RecipientEmail { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
