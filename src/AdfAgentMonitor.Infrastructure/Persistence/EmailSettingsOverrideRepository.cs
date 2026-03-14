using AdfAgentMonitor.Core.Entities;
using AdfAgentMonitor.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AdfAgentMonitor.Infrastructure.Persistence;

public class EmailSettingsOverrideRepository(
    AppDbContext db,
    IEncryptionService encryption) : IEmailSettingsOverrideRepository
{
    public async Task<EmailSettingsOverride?> GetAsync(CancellationToken ct = default)
    {
        var row = await db.EmailSettingsOverrides.FirstOrDefaultAsync(ct);
        if (row is null) return null;

        // Decrypt password for in-memory use; leave the DB column as ciphertext.
        if (row.Password is not null)
            row.Password = encryption.Decrypt(row.Password);

        return row;
    }

    public async Task SaveAsync(EmailSettingsOverride overrides, CancellationToken ct = default)
    {
        var existing = await db.EmailSettingsOverrides.FirstOrDefaultAsync(ct);
        if (existing is null)
        {
            overrides.Id       = 1;
            overrides.Password = overrides.Password is not null
                ? encryption.Encrypt(overrides.Password)
                : null;
            db.EmailSettingsOverrides.Add(overrides);
        }
        else
        {
            existing.SmtpHost         = overrides.SmtpHost;
            existing.SmtpPort         = overrides.SmtpPort;
            existing.UseSsl           = overrides.UseSsl;
            existing.Username         = overrides.Username;
            // Only update password when a new value is explicitly provided.
            if (overrides.Password is not null)
                existing.Password     = encryption.Encrypt(overrides.Password);
            existing.FromAddress      = overrides.FromAddress;
            existing.FromName         = overrides.FromName;
            existing.DashboardBaseUrl = overrides.DashboardBaseUrl;
            existing.UpdatedAt        = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        var existing = await db.EmailSettingsOverrides.FirstOrDefaultAsync(ct);
        if (existing is not null)
        {
            db.EmailSettingsOverrides.Remove(existing);
            await db.SaveChangesAsync(ct);
        }
    }
}
