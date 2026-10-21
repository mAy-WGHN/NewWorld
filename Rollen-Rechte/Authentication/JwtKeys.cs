using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Rollen_Rechte.Authentication;

/// <summary>Signier- und Prüfschlüssel samt Algorithmus; RS256 mit RSA-Schlüsseln, sonst HS256.</summary>
public sealed class JwtKeys
{
    public required SecurityKey SigningKey { get; init; }
    public required SecurityKey ValidationKey { get; init; }
    public required string Algorithm { get; init; }

    public static JwtKeys Create(JwtSettings settings)
    {
        var privatePem = ReadPem(settings.PrivateKey, settings.PrivateKeyPath);
        if (privatePem is not null)
        {
            var privateRsa = RSA.Create();
            if (string.IsNullOrEmpty(settings.Passphrase)) privateRsa.ImportFromPem(privatePem);
            else privateRsa.ImportFromEncryptedPem(privatePem, settings.Passphrase);

            var publicPem = ReadPem(settings.PublicKey, settings.PublicKeyPath);
            SecurityKey validation = new RsaSecurityKey(privateRsa);
            if (publicPem is not null)
            {
                var publicRsa = RSA.Create();
                publicRsa.ImportFromPem(publicPem);
                validation = new RsaSecurityKey(publicRsa);
            }

            return new JwtKeys
            {
                SigningKey = new RsaSecurityKey(privateRsa),
                ValidationKey = validation,
                Algorithm = SecurityAlgorithms.RsaSha256
            };
        }

        if (settings.Key is { Length: >= 32 })
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Key));
            return new JwtKeys { SigningKey = key, ValidationKey = key, Algorithm = SecurityAlgorithms.HmacSha256 };
        }

        throw new InvalidOperationException(
            "JWT-Schlüssel fehlen: Jwt__PrivateKey/Jwt__PrivateKeyPath (RSA) oder Jwt__Key (mind. 32 Zeichen) setzen.");
    }

    private static string? ReadPem(string? content, string? path)
    {
        if (!string.IsNullOrWhiteSpace(content)) return content.Replace("\\n", "\n");
        if (!string.IsNullOrWhiteSpace(path)) return File.ReadAllText(path);
        return null;
    }
}
