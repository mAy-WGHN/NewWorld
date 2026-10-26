using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Shared.Authorization;

/// <summary>Names of the services (used as token audience and in <c>ServiceAuth:Peers</c>).</summary>
public static class ServiceNames
{
    public const string Access = "access";
    public const string BcIntegration = "bc-integration";
    public const string Notifications = "notifications";
    public const string Workforce = "workforce";
    public const string Portal = "portal";
}

public static class ServiceTokens
{
    /// <summary>Issuer of service tokens is <c>svc:&lt;name&gt;</c>; this keeps them apart from person tokens issued by Access.</summary>
    public const string IssuerPrefix = "svc:";

    public const string Algorithm = SecurityAlgorithms.EcdsaSha256; // ES256

    /// <summary>Lifetime of a token. A new one is signed per call, so this only needs to cover clock differences and transport.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    /// <summary>Longest lifetime a receiver accepts, whatever the signer put into the token.</summary>
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);
}

/// <summary>Signs short-lived tokens for calls to other services with this service's private key.</summary>
public sealed class ServiceTokenIssuer(ServiceAuthOptions options, TimeProvider time) : IDisposable
{
    private readonly JsonWebTokenHandler _handler = new();
    private readonly object _gate = new();
    private System.Security.Cryptography.ECDsa? _ecdsa;
    private SigningCredentials? _credentials;

    /// <summary>A token for <paramref name="audience"/> (the target service's name). Fresh id per call.</summary>
    public string Create(string audience)
    {
        if (string.IsNullOrWhiteSpace(options.Name))
        {
            throw new InvalidOperationException($"{ServiceAuthOptions.ConfigurationSection}:Name is not configured.");
        }

        var now = time.GetUtcNow().UtcDateTime;
        return _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = ServiceTokens.IssuerPrefix + options.Name,
            Audience = audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now + ServiceTokens.Lifetime,
            Claims = new Dictionary<string, object> { ["jti"] = Guid.NewGuid().ToString("N") },
            SigningCredentials = Credentials(),
        });
    }

    public void Dispose() => _ecdsa?.Dispose();

    private SigningCredentials Credentials()
    {
        lock (_gate)
        {
            if (_credentials is null)
            {
                if (string.IsNullOrWhiteSpace(options.PrivateKey))
                {
                    throw new InvalidOperationException($"{ServiceAuthOptions.ConfigurationSection}:PrivateKey is not configured.");
                }

                _ecdsa = ServiceKeys.LoadPrivate(options.PrivateKey, $"{ServiceAuthOptions.ConfigurationSection}:PrivateKey");
                _credentials = new SigningCredentials(new ECDsaSecurityKey(_ecdsa), ServiceTokens.Algorithm);
            }

            return _credentials;
        }
    }
}
