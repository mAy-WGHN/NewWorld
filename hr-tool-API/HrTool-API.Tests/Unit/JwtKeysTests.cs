using System.Security.Cryptography;
using HrTool_API.Authentication;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace HrTool_API.Tests.Unit;

/// <summary>Das HR-Tool prüft nur Tokens von Rollen-Rechte (öffentlicher Schlüssel bzw. HS256-Fallback), es stellt keine aus.</summary>
public class JwtKeysTests
{
    private static string Token(SecurityKey signingKey, string algorithm) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Claims = new Dictionary<string, object> { ["username"] = "admin" },
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(signingKey, algorithm)
        });

    private static async Task<bool> IsValidAsync(string token, JwtKeys keys) =>
        (await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            IssuerSigningKey = keys.ValidationKey,
            ValidAlgorithms = [keys.Algorithm]
        })).IsValid;

    [Fact]
    public async Task PublicKey_ValidatesRs256Tokens_OfTheIssuer()
    {
        using var rsa = RSA.Create(2048);

        var keys = JwtKeys.Create(new JwtSettings { PublicKey = rsa.ExportSubjectPublicKeyInfoPem() });

        Assert.Equal(SecurityAlgorithms.RsaSha256, keys.Algorithm);
        Assert.True(await IsValidAsync(Token(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256), keys));
    }

    [Fact]
    public void PemWithLiteralBackslashN_AsFoundInEnvironmentVariables_IsAccepted()
    {
        using var rsa = RSA.Create(2048);
        var pem = rsa.ExportSubjectPublicKeyInfoPem().Replace("\r\n", "\n").Replace("\n", "\n");

        var keys = JwtKeys.Create(new JwtSettings { PublicKey = pem });

        Assert.Equal(SecurityAlgorithms.RsaSha256, keys.Algorithm);
    }

    [Fact]
    public void PublicKeyFile_IsRead()
    {
        using var rsa = RSA.Create(2048);
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, rsa.ExportSubjectPublicKeyInfoPem());

            Assert.Equal(SecurityAlgorithms.RsaSha256, JwtKeys.Create(new JwtSettings { PublicKeyPath = path }).Algorithm);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SymmetricFallback_UsesHs256()
    {
        var keys = JwtKeys.Create(new JwtSettings { Key = new string('k', 40) });

        Assert.Equal(SecurityAlgorithms.HmacSha256, keys.Algorithm);
        Assert.True(await IsValidAsync(Token(keys.ValidationKey, keys.Algorithm), keys));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("zu-kurz")]
    public void WithoutUsableKeyMaterial_StartupFails(string? key)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => JwtKeys.Create(new JwtSettings { Key = key }));

        Assert.Contains("JWT-Schlüssel fehlen", ex.Message);
    }

    [Fact]
    public async Task TokenSignedWithAnotherKey_IsRejected()
    {
        var signing = JwtKeys.Create(new JwtSettings { Key = new string('a', 40) });
        var validating = JwtKeys.Create(new JwtSettings { Key = new string('b', 40) });

        Assert.False(await IsValidAsync(Token(signing.ValidationKey, signing.Algorithm), validating));
    }
}
