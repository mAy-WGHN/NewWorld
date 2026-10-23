using System.Security.Cryptography;
using System.Text;

namespace Access.Core.Login;

/// <summary>
/// Encrypts the PIN reversibly (AES-256-GCM) for the stored credential. The key lives outside the database; without it nothing can
/// be read. Format: Base64(nonce 12 | tag 16 | cipher text). Changing the key makes old values unreadable (<see cref="Unprotect"/>
/// returns null); the next successful login stores a new one.
/// </summary>
public sealed class PinProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[]? _key;

    public PinProtector(string? base64Key) => _key = Parse(base64Key);

    public bool IsConfigured => _key is not null;

    public string Protect(string pin)
    {
        var key = _key ?? throw new InvalidOperationException("The PIN encryption key is not configured.");
        var plain = Encoding.UTF8.GetBytes(pin);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plain, cipher, tag);
        return Convert.ToBase64String([.. nonce, .. tag, .. cipher]);
    }

    /// <returns>The PIN, or <c>null</c> for a missing or wrong key or a manipulated value.</returns>
    public string? Unprotect(string protectedPin)
    {
        if (_key is null)
        {
            return null;
        }

        try
        {
            var data = Convert.FromBase64String(protectedPin);
            if (data.Length < NonceSize + TagSize)
            {
                return null;
            }

            var plain = new byte[data.Length - NonceSize - TagSize];
            using var aes = new AesGcm(_key, TagSize);
            aes.Decrypt(data.AsSpan(0, NonceSize), data.AsSpan(NonceSize + TagSize), data.AsSpan(NonceSize, TagSize), plain);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return null;
        }
    }

    private static byte[]? Parse(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            return null;
        }

        try
        {
            var key = Convert.FromBase64String(base64);
            return key.Length == 32 ? key : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
