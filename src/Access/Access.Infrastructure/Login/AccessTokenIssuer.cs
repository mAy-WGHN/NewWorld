using System.Security.Cryptography;
using Access.Core.Login;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Access.Infrastructure.Login;

public sealed class JwtOptions
{
    public const string ConfigurationSection = "Access:Login:Jwt";

    public string Issuer { get; set; } = "newworld-access";

    public string Audience { get; set; } = "newworld";

    /// <summary>
    /// RSA private key (PEM, PKCS#8) of the signing key. From user secrets / Key Vault, never from files in the repository.
    /// Without it Development and Testing generate a key per start (tokens end with the process); other environments refuse to start.
    /// </summary>
    public string? PrivateKeyPem { get; set; }
}

/// <summary>
/// Issues the access tokens (RS256) and publishes the public key (JWKS). Other services validate tokens locally with that key and
/// never call Access for it. Claims: <c>sub</c> and <c>employee_id</c> = EmployeeId only; permissions come from the local ACL copy.
/// </summary>
public sealed partial class AccessTokenIssuer : IDisposable
{
    private readonly RSA _rsa;
    private readonly JwtOptions _options;
    private readonly LoginOptions _login;
    private readonly TimeProvider _time;

    public AccessTokenIssuer(JwtOptions options, LoginOptions login, TimeProvider time, bool allowEphemeralKey, ILogger<AccessTokenIssuer> logger)
    {
        _options = options;
        _login = login;
        _time = time;
        _rsa = LoadKey(options, allowEphemeralKey, logger);

        if (_rsa.KeySize < 2048)
        {
            throw new InvalidOperationException("The signing key must have at least 2048 bits.");
        }

        SigningKey = new RsaSecurityKey(_rsa.ExportParameters(includePrivateParameters: true));
        SigningKey.KeyId = Base64UrlEncoder.Encode(SHA256.HashData(_rsa.ExportSubjectPublicKeyInfo()))[..16];
    }

    public RsaSecurityKey SigningKey { get; }

    private static RSA LoadKey(JwtOptions options, bool allowEphemeralKey, ILogger logger)
    {
        if (!string.IsNullOrWhiteSpace(options.PrivateKeyPem))
        {
            var rsa = RSA.Create();
            rsa.ImportFromPem(options.PrivateKeyPem);
            return rsa;
        }

        if (!allowEphemeralKey)
        {
            throw new InvalidOperationException($"{JwtOptions.ConfigurationSection}:PrivateKeyPem is not configured.");
        }

        LogEphemeralKey(logger);
        return RSA.Create(2048);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No signing key configured: using a key that ends with this process (Development/Testing only).")]
    private static partial void LogEphemeralKey(ILogger logger);

    public string Issuer => _options.Issuer;

    public string Audience => _options.Audience;

    /// <summary>Parameters for validating tokens issued here (used by Access itself).</summary>
    public TokenValidationParameters ValidationParameters => new()
    {
        ValidIssuer = Issuer,
        ValidAudience = Audience,
        IssuerSigningKey = SigningKey,
        ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
        ValidateIssuerSigningKey = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = "sub",
    };

    public IssuedToken Issue(Guid employeeId)
    {
        var now = _time.GetUtcNow();
        var expires = now + _login.AccessTokenLifetime;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            Claims = new Dictionary<string, object> { ["sub"] = employeeId.ToString(), ["employee_id"] = employeeId.ToString() },
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.RsaSha256),
        };
        return new IssuedToken(new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(descriptor), expires);
    }

    /// <summary>The public key as JWKS document (public parts only).</summary>
    public object Jwks()
    {
        var parameters = _rsa.ExportParameters(includePrivateParameters: false);
        return new
        {
            keys = new[]
            {
                new
                {
                    kty = "RSA",
                    use = "sig",
                    alg = SecurityAlgorithms.RsaSha256,
                    kid = SigningKey.KeyId,
                    n = Base64UrlEncoder.Encode(parameters.Modulus),
                    e = Base64UrlEncoder.Encode(parameters.Exponent),
                },
            },
        };
    }

    public void Dispose() => _rsa.Dispose();
}

public sealed record IssuedToken(string Token, DateTimeOffset ExpiresAt);
