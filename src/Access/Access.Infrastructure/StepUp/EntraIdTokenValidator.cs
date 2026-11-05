using Access.Core.StepUp;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Access.Infrastructure.StepUp;

/// <summary>Who Entra says signed in: the immutable object id and the user principal name.</summary>
public sealed record EntraIdentity(Guid ObjectId, string UserPrincipalName);

/// <summary>Why an ID token was refused. Only these codes are logged, never token contents.</summary>
public enum IdTokenFailure
{
    None = 0,
    Invalid = 1,
    Expired = 2,
    TenantMismatch = 3,
    NonceMismatch = 4,
    MfaMissing = 5,
    IdentityMissing = 6,
}

public sealed record IdTokenResult(EntraIdentity? Identity, IdTokenFailure Failure)
{
    public static IdTokenResult Fail(IdTokenFailure failure) => new(null, failure);
}

/// <summary>
/// Validates the ID token of the step-up: signature (keys of the tenant's discovery document), issuer, audience (our client id), tenant
/// (<c>tid</c>), lifetime (with the <see cref="TimeProvider"/>), the nonce of this request and, fail closed, <c>amr</c> containing <c>mfa</c>.
/// </summary>
public sealed class EntraIdTokenValidator(EntraOidcClient entra, StepUpOptions options, TimeProvider time)
{
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(1);
    private static readonly string[] Rs256 = [SecurityAlgorithms.RsaSha256];

    // Names of claims of Entra ID tokens (not our own token claims).
    private const string ObjectIdClaim = "oid";
    private const string TenantClaim = "tid";
    private const string NonceClaim = "nonce";
    private const string AuthenticationMethodsClaim = "amr";
    private static readonly string[] UserNameClaims = ["preferred_username", "upn"];

    public async Task<IdTokenResult> ValidateAsync(string idToken, string expectedNonce, CancellationToken cancellationToken)
    {
        var handler = new JsonWebTokenHandler();
        var configuration = await entra.GetConfigurationAsync(cancellationToken);
        var result = await handler.ValidateTokenAsync(idToken, Parameters(configuration.SigningKeys));
        if (!result.IsValid && result.Exception is SecurityTokenSignatureKeyNotFoundException)
        {
            // The provider may have rolled its keys: look again once (the manager limits how often it really asks).
            entra.RequestRefresh();
            configuration = await entra.GetConfigurationAsync(cancellationToken);
            result = await handler.ValidateTokenAsync(idToken, Parameters(configuration.SigningKeys));
        }

        if (!result.IsValid || result.SecurityToken is not JsonWebToken jwt)
        {
            return IdTokenResult.Fail(IdTokenFailure.Invalid);
        }

        // Lifetime by hand so that it runs on the TimeProvider; a token without expiry is refused.
        var now = time.GetUtcNow().UtcDateTime;
        if (jwt.ValidTo == DateTime.MinValue || now > jwt.ValidTo + ClockSkew || now < jwt.ValidFrom - ClockSkew)
        {
            return IdTokenResult.Fail(IdTokenFailure.Expired);
        }

        if (!Text(jwt, TenantClaim).Equals(options.TenantId, StringComparison.OrdinalIgnoreCase))
        {
            return IdTokenResult.Fail(IdTokenFailure.TenantMismatch);
        }

        if (!string.Equals(Text(jwt, NonceClaim), expectedNonce, StringComparison.Ordinal))
        {
            return IdTokenResult.Fail(IdTokenFailure.NonceMismatch);
        }

        if (!jwt.Claims.Any(c => c.Type == AuthenticationMethodsClaim && string.Equals(c.Value, "mfa", StringComparison.OrdinalIgnoreCase)))
        {
            return IdTokenResult.Fail(IdTokenFailure.MfaMissing);
        }

        var userName = UserNameClaims.Select(name => Text(jwt, name)).FirstOrDefault(v => v.Length > 0) ?? "";
        return Guid.TryParse(Text(jwt, ObjectIdClaim), out var objectId) && objectId != Guid.Empty && userName.Length > 0
            ? new IdTokenResult(new EntraIdentity(objectId, userName), IdTokenFailure.None)
            : IdTokenResult.Fail(IdTokenFailure.IdentityMissing);
    }

    private TokenValidationParameters Parameters(ICollection<SecurityKey> keys) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = options.EffectiveAuthority,
        ValidateAudience = true,
        ValidAudience = options.ClientId,
        ValidateIssuerSigningKey = true,
        IssuerSigningKeys = keys,
        RequireSignedTokens = true,
        ValidAlgorithms = Rs256,
        ValidateLifetime = false, // see ValidateAsync
    };

    private static string Text(JsonWebToken jwt, string claim) => jwt.TryGetPayloadValue<string>(claim, out var value) ? value ?? "" : "";
}
