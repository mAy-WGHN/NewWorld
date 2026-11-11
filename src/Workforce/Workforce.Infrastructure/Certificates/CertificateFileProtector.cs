using System.Security.Cryptography;

namespace Workforce.Infrastructure.Certificates;

/// <summary>
/// Encrypts the content of a certificate before it reaches the storage (AES-256-GCM). The document id is bound to the ciphertext
/// (associated data), so a stored file cannot be moved to another document.
/// <para>
/// Format 2: version (1 byte), key id (1), nonce (12), tag (16), ciphertext. The key id says which key of the key ring encrypted the file,
/// so a key can be replaced without making old files unreadable: the new key becomes the current one, the old one stays in the ring until
/// <see cref="NeedsRotation"/> is false for every file (the maintenance re-encrypts in the background). Format 1 (no key id, from before the
/// ring) is still read, by trying the keys of the ring, and counts as "needs rotation".
/// </para>
/// The keys come from configuration (User Secrets / Key Vault), never from the repository; losing a key makes the files under it unreadable.
/// </summary>
public sealed class CertificateFileProtector
{
    /// <summary>The key id of a single key without a ring (the setting <c>EncryptionKeyId</c> defaults to it).</summary>
    public const byte DefaultKeyId = 1;

    private const byte LegacyVersion = 1;
    private const byte Version = 2;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int HeaderSize = 1 + 1 + NonceSize + TagSize;
    private const int LegacyHeaderSize = 1 + NonceSize + TagSize;

    private readonly byte _currentId;
    private readonly Dictionary<byte, byte[]> _keys;

    public CertificateFileProtector(byte[] key) : this(DefaultKeyId, key, new Dictionary<byte, byte[]>())
    {
    }

    /// <param name="currentKeyId">Id of the key that encrypts from now on.</param>
    /// <param name="currentKey">The current key (32 bytes).</param>
    /// <param name="previousKeys">Older keys by id: only for reading (and for re-encrypting); never used to encrypt.</param>
    public CertificateFileProtector(byte currentKeyId, byte[] currentKey, IReadOnlyDictionary<byte, byte[]> previousKeys)
    {
        _currentId = currentKeyId;
        _keys = [];
        Add(currentKeyId, currentKey);
        foreach (var (id, key) in previousKeys)
        {
            if (id == currentKeyId)
            {
                throw new ArgumentException("A previous key has the id of the current key.", nameof(previousKeys));
            }

            Add(id, key);
        }
    }

    private void Add(byte id, byte[] key)
    {
        if (key.Length != 32)
        {
            throw new ArgumentException("A key must be 32 bytes (AES-256).", nameof(key));
        }

        _keys[id] = key;
    }

    /// <summary>Whether there are older keys in the ring, i.e. a rotation may be going on.</summary>
    public bool HasPreviousKeys => _keys.Count > 1;

    /// <summary>A new random key in the configuration format (base64).</summary>
    public static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    /// <summary>Reads the key from its configuration value; the error names the setting only, never the value.</summary>
    public static CertificateFileProtector FromConfiguration(string? base64, string setting) => FromConfiguration(base64, DefaultKeyId, new Dictionary<byte, string>(), setting);

    /// <summary>
    /// Reads the current key and the previous keys (id → base64) from their configuration values; the error names the setting only, never a value.
    /// </summary>
    public static CertificateFileProtector FromConfiguration(string? currentBase64, byte currentKeyId, IReadOnlyDictionary<byte, string> previousBase64, string setting)
    {
        try
        {
            return new CertificateFileProtector(
                currentKeyId, Convert.FromBase64String(currentBase64 ?? ""), previousBase64.ToDictionary(p => p.Key, p => Convert.FromBase64String(p.Value)));
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            throw new InvalidOperationException($"{setting} (the current key and the previous keys) must be base64 encoded 32 byte keys with different ids.");
        }
    }

    public byte[] Protect(byte[] content, Guid documentId)
    {
        var result = new byte[HeaderSize + content.Length];
        result[0] = Version;
        result[1] = _currentId;
        var nonce = result.AsSpan(2, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_keys[_currentId], TagSize);
        aes.Encrypt(nonce, content, result.AsSpan(HeaderSize), result.AsSpan(2 + NonceSize, TagSize), documentId.ToByteArray());
        return result;
    }

    public byte[] Unprotect(byte[] data, Guid documentId)
    {
        if (data.Length >= HeaderSize && data[0] == Version)
        {
            if (!_keys.TryGetValue(data[1], out var key))
            {
                throw new CryptographicException("The stored file was encrypted with a key that is not configured.");
            }

            return Decrypt(key, data.AsSpan(2, NonceSize), data.AsSpan(HeaderSize), data.AsSpan(2 + NonceSize, TagSize), documentId);
        }

        if (data.Length >= LegacyHeaderSize && data[0] == LegacyVersion)
        {
            // Without a key id: the current key first, then the older ones; a wrong key fails the authentication tag.
            foreach (var key in _keys.OrderBy(k => k.Key == _currentId ? 0 : 1).Select(k => k.Value))
            {
                try
                {
                    return Decrypt(key, data.AsSpan(1, NonceSize), data.AsSpan(LegacyHeaderSize), data.AsSpan(1 + NonceSize, TagSize), documentId);
                }
                catch (CryptographicException)
                {
                }
            }

            throw new CryptographicException("The stored file could not be decrypted with any configured key.");
        }

        throw new CryptographicException("The stored file has an unknown format.");
    }

    /// <summary>Whether the file is not in the current format under the current key (so the maintenance should encrypt it again).</summary>
    public bool NeedsRotation(byte[] data) => !(data.Length >= HeaderSize && data[0] == Version && data[1] == _currentId);

    private static byte[] Decrypt(byte[] key, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> cipher, ReadOnlySpan<byte> tag, Guid documentId)
    {
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain, documentId.ToByteArray());
        return plain;
    }
}
