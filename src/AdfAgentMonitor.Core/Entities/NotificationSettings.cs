namespace AdfAgentMonitor.Core.Entities;

public class NotificationSettings
{
    public int    Id               { get; set; } = 1; // always 1
    /// <summary>Comma-separated list of recipient email addresses.</summary>
    public string RecipientEmails  { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Parsed list of recipient emails. Not persisted directly — maps to <see cref="RecipientEmails"/>.</summary>
    public List<string> RecipientEmailList
    {
        get => string.IsNullOrWhiteSpace(RecipientEmails)
            ? []
            : [..RecipientEmails.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
        set => RecipientEmails = string.Join(",", value);
    }
}
