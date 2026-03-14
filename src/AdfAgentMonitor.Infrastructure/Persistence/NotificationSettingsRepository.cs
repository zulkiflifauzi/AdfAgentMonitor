using AdfAgentMonitor.Core.Entities;
using AdfAgentMonitor.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AdfAgentMonitor.Infrastructure.Persistence;

public class NotificationSettingsRepository(AppDbContext db) : INotificationSettingsRepository
{
    public async Task<NotificationSettings> GetAsync(CancellationToken ct = default)
    {
        var row = await db.NotificationSettings.FirstOrDefaultAsync(ct);
        return row ?? new NotificationSettings();
    }

    public async Task SaveAsync(NotificationSettings settings, CancellationToken ct = default)
    {
        var existing = await db.NotificationSettings.FirstOrDefaultAsync(ct);
        if (existing is null)
        {
            settings.Id = 1;
            db.NotificationSettings.Add(settings);
        }
        else
        {
            existing.RecipientEmail = settings.RecipientEmail;
            existing.UpdatedAt      = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(ct);
    }
}
