using AdfAgentMonitor.Core.Entities;

namespace AdfAgentMonitor.Core.Interfaces;

public interface INotificationSettingsRepository
{
    Task<NotificationSettings> GetAsync(CancellationToken ct = default);
    Task SaveAsync(NotificationSettings settings, CancellationToken ct = default);
}
