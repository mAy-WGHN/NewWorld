using System.Security.Cryptography;

namespace Workforce.Infrastructure.Certificates;

/// <summary>
/// Encrypts the content of a certificate before it reaches the database (AES-256-GCM). The document id is bound to the ciphertext
/// (associated data), so a stored file cannot be moved to another document. Format: version (1 byte), nonce (12), tag (16), ciphertext.
/// The key comes from configuration (User Secrets / Key Vault), never from the repository; losing it makes the files unreadable.
/// </summary>
public sealed class CertificateFileProtector
{
    private const byte Version = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key;

    public CertificateFileProtector(byte[] key)
    {
        if (key.Length != 32)
        {
            throw new ArgumentException("The key must be 32 bytes (AES-256).", nameof(key));
        }

        _key = key;
    }

    /// <summary>A new random key in the configuration format (base64).</summary>
    public static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    /// <summary>Reads the key from its configuration value; the error names the setting only, never the value.</summary>
    public static CertificateFileProtector FromConfiguration(string? base64, string setting)
    {
        try
        {
            return new CertificateFileProtector(Convert.FromBase64String(base64 ?? ""));
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            throw new InvalidOperationException($"{setting} must be a base64 encoded 32 byte key.");
        }
    }

    public byte[] Protect(byte[] content, Guid documentId)
    {
        var result = new byte[1 + NonceSize + TagSize + content.Length];
        result[0] = Version;
        var nonce = result.AsSpan(1, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, content, result.AsSpan(1 + NonceSize + TagSize), result.AsSpan(1 + NonceSize, TagSize), documentId.ToByteArray());
        return result;
    }

    public byte[] Unprotect(byte[] data, Guid documentId)
    {
        if (data.Length < 1 + NonceSize + TagSize || data[0] != Version)
        {
            throw new CryptographicException("The stored file has an unknown format.");
        }

        var plain = new byte[data.Length - 1 - NonceSize - TagSize];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(data.AsSpan(1, NonceSize), data.AsSpan(1 + NonceSize + TagSize), data.AsSpan(1 + NonceSize, TagSize), plain, documentId.ToByteArray());
        return plain;
    }
}
