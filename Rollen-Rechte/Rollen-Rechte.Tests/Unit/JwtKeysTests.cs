using System.Security.Cryptography;
using Rollen_Rechte.Authentication;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Rollen_Rechte.Tests.Unit;

public class JwtKeysTests
{
    private static async Task<bool> RoundTripAsync(JwtKeys keys)
    {
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Claims = new Dictionary<string, object> { ["username"] = "admin" },
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(keys.SigningKey, keys.Algorithm)
        });

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            IssuerSigningKey = keys.ValidationKey,
            ValidAlgorithms = [keys.Algorithm]
        });
        return result.IsValid;
    }

    [Fact]
    public async Task EncryptedRsaPrivateKey_WithPassphrase_UsesRs256_LikeLexik()
    {
        using var rsa = RSA.Create(2048);
        var pem = rsa.ExportEncryptedPkcs8PrivateKeyPem("passphrase",
            new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 1000));

        var keys = JwtKeys.Create(new JwtSettings { PrivateKey = pem, Passphrase = "passphrase" });

        Assert.Equal(SecurityAlgorithms.RsaSha256, keys.Algorithm);
        Assert.True(await RoundTripAsync(keys));
    }

    [Fact]
    public async Task SeparatePublicKey_IsUsedForValidation()
    {
        using var rsa = RSA.Create(2048);
        var settings = new JwtSettings { PrivateKey = rsa.ExportPkcs8PrivateKeyPem(), PublicKey = rsa.ExportSubjectPublicKeyInfoPem() };

        var keys = JwtKeys.Create(settings);

        Assert.True(await RoundTripAsync(keys));
        Assert.NotSame(keys.SigningKey, keys.ValidationKey);
    }

    [Fact]
    public void PemWithLiteralBackslashN_AsFoundInEnvironmentVariables_IsAccepted()
    {
        using var rsa = RSA.Create(2048);
        var pem = rsa.ExportPkcs8PrivateKeyPem().Replace("\r\n", "\\n").Replace("\n", "\\n");

        var keys = JwtKeys.Create(new JwtSettings { PrivateKey = pem });

        Assert.Equal(SecurityAlgorithms.RsaSha256, keys.Algorithm);
    }

    [Fact]
    public void PemFromFile_IsAccepted()
    {
        using var rsa = RSA.Create(2048);
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, rsa.ExportPkcs8PrivateKeyPem());

            var keys = JwtKeys.Create(new JwtSettings { PrivateKeyPath = path });

            Assert.Equal(SecurityAlgorithms.RsaSha256, keys.Algorithm);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task WrongPassphrase_Fails()
    {
        using var rsa = RSA.Create(2048);
        var pem = rsa.ExportEncryptedPkcs8PrivateKeyPem("richtig",
            new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 1000));

        Assert.ThrowsAny<CryptographicException>(() => JwtKeys.Create(new JwtSettings { PrivateKey = pem, Passphrase = "falsch" }));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task SymmetricKeyFallback_UsesHs256()
    {
        var keys = JwtKeys.Create(new JwtSettings { Key = new string('k', 40) });

        Assert.Equal(SecurityAlgorithms.HmacSha256, keys.Algorithm);
        Assert.True(await RoundTripAsync(keys));
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
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Claims = new Dictionary<string, object> { ["username"] = "x" },
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(signing.SigningKey, signing.Algorithm)
        });

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidateIssuer = false, ValidateAudience = false, IssuerSigningKey = validating.ValidationKey
        });

        Assert.False(result.IsValid);
    }
}
