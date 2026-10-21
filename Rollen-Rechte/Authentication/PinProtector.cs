using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Rollen_Rechte.Authentication;

/// <summary>
/// Verschlüsselt das BC-PIN reversibel (AES-256-GCM) für <see cref="Domain.Entities.User.BcPinEncrypted"/>.
/// Der Schlüssel kommt aus <c>Auth__PinEncryptionKey</c> (Base64, 32 Bytes) und liegt nie in der DB; wer ihn
/// hat, kann die Werte mit "decrypt-pin" wieder lesbar machen. Format: Base64(Nonce 12 | Tag 16 | Chiffrat).
/// Wird der Schlüssel gewechselt, lassen sich alte Werte nicht mehr entschlüsseln (Unprotect liefert null).
/// </summary>
public class PinProtector(IOptions<AuthOptions> options)
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[]? _key = ParseKey(options.Value.PinEncryptionKey);

    public bool IsConfigured => _key is not null;

    public string Protect(string pin)
    {
        var key = _key ?? throw new InvalidOperationException("Auth:PinEncryptionKey ist nicht gesetzt.");
        var plain = Encoding.UTF8.GetBytes(pin);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plain, cipher, tag);

        return Convert.ToBase64String([.. nonce, .. tag, .. cipher]);
    }

    /// <summary>Liefert null bei fehlendem Schlüssel, falschem Schlüssel oder manipuliertem Wert.</summary>
    public string? Unprotect(string protectedPin)
    {
        if (_key is null) return null;

        try
        {
            var data = Convert.FromBase64String(protectedPin);
            if (data.Length < NonceSize + TagSize) return null;

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

    private static byte[]? ParseKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        try
        {
            var key = Convert.FromBase64String(value.Trim());
            return key.Length == 32 ? key : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
