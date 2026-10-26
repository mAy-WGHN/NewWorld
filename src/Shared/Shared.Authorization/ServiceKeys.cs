using System.Security.Cryptography;

namespace Shared.Authorization;

/// <summary>An ECDSA P-256 key pair in the configuration format (base64): private key PKCS#8, public key SubjectPublicKeyInfo.</summary>
public sealed record ServiceKeyPair(string PrivateKey, string PublicKey);

public static class ServiceKeys
{
    /// <summary>Creates a new key pair. The private key stays with the owning service only, the public key goes to the services that accept it.</summary>
    public static ServiceKeyPair Generate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return new ServiceKeyPair(Convert.ToBase64String(key.ExportPkcs8PrivateKey()), Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
    }

    // Error messages name the setting only, never the value (it may be a secret).
    internal static ECDsa LoadPrivate(string base64, string setting) => Load(base64, setting, (key, bytes) => key.ImportPkcs8PrivateKey(bytes, out _));

    internal static ECDsa LoadPublic(string base64, string setting) => Load(base64, setting, (key, bytes) => key.ImportSubjectPublicKeyInfo(bytes, out _));

    private static ECDsa Load(string base64, string setting, Action<ECDsa, byte[]> import)
    {
        var key = ECDsa.Create();
        try
        {
            import(key, Convert.FromBase64String(base64));
            if (key.KeySize != 256)
            {
                throw new CryptographicException("Not a P-256 key.");
            }

            return key;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            key.Dispose();
            throw new InvalidOperationException($"{setting} is not a valid base64 ECDSA P-256 key.");
        }
    }
}
