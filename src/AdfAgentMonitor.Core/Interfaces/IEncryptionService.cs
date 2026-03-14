namespace AdfAgentMonitor.Core.Interfaces;

public interface IEncryptionService
{
    /// <summary>Encrypts <paramref name="plaintext"/> and returns a base64-encoded ciphertext.</summary>
    string Encrypt(string plaintext);

    /// <summary>
    /// Decrypts a value produced by <see cref="Encrypt"/>.
    /// Returns <c>null</c> if decryption fails (e.g. key was rotated or value is corrupted)
    /// so callers can fall back gracefully.
    /// </summary>
    string? Decrypt(string ciphertext);
}
