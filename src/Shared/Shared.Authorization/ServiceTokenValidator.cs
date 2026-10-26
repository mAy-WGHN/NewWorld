using System.Collections.Concurrent;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Shared.Authorization;

/// <summary>Result of checking a service token. <see cref="Failure"/> is a short code for the log, never shown to the caller.</summary>
public sealed record ServiceTokenResult(bool Succeeded, string? Service, IReadOnlyList<string> Roles, string? Failure)
{
    public static ServiceTokenResult Fail(string reason) => new(false, null, [], reason);
}

/// <summary>
/// Checks the tokens other services send: issuer is a configured peer, signature with one of that peer's public keys (ES256 only),
/// audience is this service, short lifetime, and every token only once (replay protection per instance). The roles a peer
/// gets are exactly those configured for it. Configuration errors surface at construction (the host checks it at start).
/// </summary>
public sealed class ServiceTokenValidator
{
    private readonly string? _name;
    private readonly Dictionary<string, Peer> _peers = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;
    private readonly JsonWebTokenHandler _handler = new() { MapInboundClaims = false };
    private readonly ConcurrentDictionary<string, DateTimeOffset> _seen = new(StringComparer.Ordinal);

    private sealed record Peer(IReadOnlyList<SecurityKey> Keys, IReadOnlyList<string> Roles);

    public ServiceTokenValidator(ServiceAuthOptions options, SharedAuthorizationOptions apiRoles, TimeProvider time)
    {
        _time = time;
        _name = options.Name;
        if (options.Peers.Count > 0 && string.IsNullOrWhiteSpace(_name))
        {
            throw new InvalidOperationException($"{ServiceAuthOptions.ConfigurationSection}:Name is not configured (needed to accept peers).");
        }

        foreach (var (name, peer) in options.Peers)
        {
            var setting = $"{ServiceAuthOptions.ConfigurationSection}:Peers:{name}";
            if (peer.PublicKeys.Count == 0)
            {
                throw new InvalidOperationException($"{setting}:PublicKeys has no key.");
            }

            var keys = peer.PublicKeys
                .Select((key, index) => (SecurityKey)new ECDsaSecurityKey(ServiceKeys.LoadPublic(key, $"{setting}:PublicKeys:{index}")))
                .ToList();
            foreach (var role in peer.Roles.Where(role => !apiRoles.ServiceRoles.Contains(role)))
            {
                throw new InvalidOperationException($"{setting}:Roles grants '{role}', which this API does not know.");
            }

            _peers[name] = new Peer(keys, [.. peer.Roles.Distinct(StringComparer.Ordinal)]);
        }
    }

    /// <summary>True if the Authorization header carries a token that claims to be a service token (issuer prefix). Does not check it.</summary>
    public bool IsServiceToken(string? authorizationHeader) =>
        TryRead(BearerToken(authorizationHeader), out var token)
        && token.Issuer.StartsWith(ServiceTokens.IssuerPrefix, StringComparison.Ordinal);

    public async Task<ServiceTokenResult> ValidateAsync(string bearerToken)
    {
        if (!TryRead(bearerToken, out var unverified))
        {
            return ServiceTokenResult.Fail("malformed");
        }

        if (!unverified.Issuer.StartsWith(ServiceTokens.IssuerPrefix, StringComparison.Ordinal)
            || !_peers.TryGetValue(unverified.Issuer[ServiceTokens.IssuerPrefix.Length..], out var peer))
        {
            return ServiceTokenResult.Fail("unknown-issuer");
        }

        var name = unverified.Issuer[ServiceTokens.IssuerPrefix.Length..];
        var result = await _handler.ValidateTokenAsync(bearerToken, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = unverified.Issuer,
            ValidateAudience = true,
            ValidAudience = _name,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = peer.Keys,
            ValidAlgorithms = [ServiceTokens.Algorithm],
            ClockSkew = ServiceTokens.ClockSkew,
            LifetimeValidator = (notBefore, expires, _, _) => LifetimeIsValid(notBefore, expires),
        });
        if (!result.IsValid || result.SecurityToken is not JsonWebToken token)
        {
            return ServiceTokenResult.Fail("invalid");
        }

        if (token.ValidFrom == DateTime.MinValue || token.ValidTo - token.ValidFrom > ServiceTokens.MaxLifetime)
        {
            return ServiceTokenResult.Fail("lifetime");
        }

        if (string.IsNullOrEmpty(token.Id) || !MarkUsed($"{name}:{token.Id}", new DateTimeOffset(token.ValidTo, TimeSpan.Zero)))
        {
            return ServiceTokenResult.Fail("replay");
        }

        return new ServiceTokenResult(true, name, peer.Roles, null);
    }

    /// <summary>Own lifetime check against the injected clock (not the system clock), with the clock skew of <see cref="ServiceTokens.ClockSkew"/>.</summary>
    private bool LifetimeIsValid(DateTime? notBefore, DateTime? expires)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        return notBefore is not null && expires is not null && notBefore.Value - ServiceTokens.ClockSkew <= now && now <= expires.Value + ServiceTokens.ClockSkew;
    }

    /// <summary>The token of an <c>Authorization: Bearer</c> header, or null.</summary>
    internal static string? BearerToken(string? authorizationHeader) =>
        authorizationHeader is not null && authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authorizationHeader[7..].Trim()
            : null;

    private bool TryRead(string? token, out JsonWebToken jwt)
    {
        jwt = null!;
        if (string.IsNullOrWhiteSpace(token) || !_handler.CanReadToken(token))
        {
            return false;
        }

        try
        {
            jwt = _handler.ReadJsonWebToken(token);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Remembers the token id until the token has expired; false if it was seen before.</summary>
    private bool MarkUsed(string key, DateTimeOffset expires)
    {
        var now = _time.GetUtcNow();
        if (_seen.Count > 1024)
        {
            foreach (var entry in _seen.Where(e => e.Value + ServiceTokens.ClockSkew < now))
            {
                _seen.TryRemove(entry.Key, out _);
            }
        }

        return _seen.TryAdd(key, expires);
    }
}
