using AdfAgentMonitor.Core.Entities;

namespace AdfAgentMonitor.Core.Interfaces;

public interface IEmailSettingsOverrideRepository
{
    /// <summary>Returns the stored overrides, or <c>null</c> if no row exists yet.</summary>
    Task<EmailSettingsOverride?> GetAsync(CancellationToken ct = default);

    /// <summary>Upserts the override row (Id is always forced to 1).</summary>
    Task SaveAsync(EmailSettingsOverride overrides, CancellationToken ct = default);

    /// <summary>Deletes the override row, reverting all settings to appsettings defaults.</summary>
    Task ClearAsync(CancellationToken ct = default);
}
