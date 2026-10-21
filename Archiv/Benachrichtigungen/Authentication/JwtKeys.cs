using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Benachrichtigungen.Authentication;

/// <summary>Prüfschlüssel samt Algorithmus; RS256 mit RSA-Public-Key, sonst HS256.</summary>
public sealed class JwtKeys
{
    public required SecurityKey ValidationKey { get; init; }
    public required string Algorithm { get; init; }

    public static JwtKeys Create(JwtSettings settings)
    {
        var publicPem = ReadPem(settings.PublicKey, settings.PublicKeyPath);
        if (publicPem is not null)
        {
            var rsa = RSA.Create();
            rsa.ImportFromPem(publicPem);
            return new JwtKeys { ValidationKey = new RsaSecurityKey(rsa), Algorithm = SecurityAlgorithms.RsaSha256 };
        }

        if (settings.Key is { Length: >= 32 })
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Key));
            return new JwtKeys { ValidationKey = key, Algorithm = SecurityAlgorithms.HmacSha256 };
        }

        throw new InvalidOperationException(
            "JWT-Schlüssel fehlen: Jwt__PublicKey/Jwt__PublicKeyPath (RSA) oder Jwt__Key (mind. 32 Zeichen) setzen.");
    }

    private static string? ReadPem(string? content, string? path)
    {
        if (!string.IsNullOrWhiteSpace(content)) return content.Replace("\n", "\n");
        if (!string.IsNullOrWhiteSpace(path)) return File.ReadAllText(path);
        return null;
    }
}
