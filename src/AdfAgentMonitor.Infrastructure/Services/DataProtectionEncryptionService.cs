using System.Security.Cryptography;
using AdfAgentMonitor.Core.Interfaces;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace AdfAgentMonitor.Infrastructure.Services;

public class DataProtectionEncryptionService : IEncryptionService
{
    // Purpose string scopes this protector so it cannot decrypt data protected
    // with a different purpose string within the same application.
    private const string Purpose = "AdfAgentMonitor.EmailPassword.v1";

    private readonly IDataProtector _protector;
    private readonly ILogger<DataProtectionEncryptionService> _logger;

    public DataProtectionEncryptionService(
        IDataProtectionProvider provider,
        ILogger<DataProtectionEncryptionService> logger)
    {
        _protector = provider.CreateProtector(Purpose);
        _logger    = logger;
    }

    public string Encrypt(string plaintext) => _protector.Protect(plaintext);

    public string? Decrypt(string ciphertext)
    {
        try
        {
            return _protector.Unprotect(ciphertext);
        }
        catch (CryptographicException ex)
        {
            // Key may have been rotated or the value pre-dates encryption — return null
            // so the caller falls back to the appsettings.json password.
            _logger.LogWarning(ex,
                "Failed to decrypt stored SMTP password. " +
                "The value may have been stored before encryption was enabled or the key ring has changed. " +
                "Re-enter the password in Settings → Email to re-encrypt it.");
            return null;
        }
    }
}
