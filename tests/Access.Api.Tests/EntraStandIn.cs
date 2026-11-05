using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Access.Api.Tests;

/// <summary>
/// A stand-in for the Microsoft Entra tenant (there is no real one to test against): discovery document, signing keys and the token endpoint, as an
/// <see cref="HttpMessageHandler"/> behind the HTTP client Access uses. A test "signs in" with <see cref="SignIn"/> (what the person does in the
/// browser) and chooses what the ID token says. The token endpoint enforces what Entra enforces: client secret, redirect address, PKCE verifier,
/// codes that work once.
/// </summary>
public sealed class EntraStandIn(Func<DateTimeOffset> now) : HttpMessageHandler
{
    public const string TenantId = "11111111-2222-3333-4444-555555555555";
    public const string ClientId = "22222222-3333-4444-5555-666666666666";
    public const string ClientSecret = "stand-in-client-secret-Zq9";
    public const string RedirectUri = "https://access.test/api/auth/step-up/callback";
    public static readonly string Authority = $"https://login.entra.test/{TenantId}/v2.0";

    private static readonly string[] ResponseTypes = ["code", "id_token"];
    private static readonly string[] SubjectTypes = ["pairwise"];
    private static readonly string[] SigningAlgorithms = ["RS256"];

    private readonly RSA _key = RSA.Create(2048);
    private readonly RSA _foreignKey = RSA.Create(2048);
    private readonly Dictionary<string, Pending> _codes = [];

    /// <summary>The provider is unreachable (every request fails).</summary>
    public bool Down { get; set; }

    /// <summary>Every ID token issued, so that a test can look for it in logs.</summary>
    public List<string> IdTokens { get; } = [];

    /// <summary>What the ID token for one sign-in says; the defaults are a regular sign-in with the second factor.</summary>
    public sealed record Spec
    {
        public Guid Oid { get; init; }
        public string Upn { get; init; } = "";
        public string[] Amr { get; init; } = ["pwd", "mfa"];
        public string Tenant { get; init; } = TenantId;
        public string Issuer { get; init; } = Authority;
        public string Audience { get; init; } = ClientId;

        /// <summary>Another nonce than the one of the request.</summary>
        public string? Nonce { get; init; }

        /// <summary>How long ago the token was issued; it lives 60 minutes (older than that: expired).</summary>
        public TimeSpan Age { get; init; }

        /// <summary>Signed with a key the discovery document does not list.</summary>
        public bool ForeignKey { get; init; }
    }

    private sealed record Pending(string Nonce, string Challenge, Spec Spec);

    /// <summary>The person confirms in the browser: returns the <c>code</c> and <c>state</c> Entra would send back to the redirect address.</summary>
    public (string Code, string State) SignIn(string authorizationUrl, Spec spec)
    {
        var query = QueryHelpers.ParseQuery(new Uri(authorizationUrl).Query);
        var code = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(24));
        _codes[code] = new Pending(query["nonce"].ToString(), query["code_challenge"].ToString(), spec);
        return (code, query["state"].ToString());
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Down)
        {
            throw new HttpRequestException("stand-in is down");
        }

        var path = request.RequestUri!.AbsolutePath;
        if (request.Method == HttpMethod.Get && path == $"/{TenantId}/v2.0/.well-known/openid-configuration")
        {
            return Json(new
            {
                issuer = Authority,
                authorization_endpoint = $"https://login.entra.test/{TenantId}/oauth2/v2.0/authorize",
                token_endpoint = $"https://login.entra.test/{TenantId}/oauth2/v2.0/token",
                jwks_uri = $"https://login.entra.test/{TenantId}/discovery/v2.0/keys",
                response_types_supported = ResponseTypes,
                subject_types_supported = SubjectTypes,
                id_token_signing_alg_values_supported = SigningAlgorithms,
            });
        }

        if (request.Method == HttpMethod.Get && path == $"/{TenantId}/discovery/v2.0/keys")
        {
            return Json(new { keys = new[] { Jwk(_key) } });
        }

        if (request.Method == HttpMethod.Post && path == $"/{TenantId}/oauth2/v2.0/token")
        {
            var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(cancellationToken));
            return Exchange(form);
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private HttpResponseMessage Exchange(Dictionary<string, Microsoft.Extensions.Primitives.StringValues> form)
    {
        string Field(string name) => form.TryGetValue(name, out var v) ? v.ToString() : "";

        if (Field("grant_type") != "authorization_code" || Field("client_id") != ClientId || Field("client_secret") != ClientSecret
            || Field("redirect_uri") != RedirectUri || !_codes.Remove(Field("code"), out var pending)
            || WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(Field("code_verifier")))) != pending.Challenge)
        {
            return Json(new { error = "invalid_grant" }, HttpStatusCode.BadRequest);
        }

        var spec = pending.Spec;
        var issued = now() - spec.Age;
        var claims = new Dictionary<string, object>
        {
            ["tid"] = spec.Tenant,
            ["oid"] = spec.Oid.ToString(),
            ["sub"] = "pairwise-subject",
            ["preferred_username"] = spec.Upn,
            ["nonce"] = spec.Nonce ?? pending.Nonce,
            ["amr"] = spec.Amr,
        };
        var signing = new RsaSecurityKey(spec.ForeignKey ? _foreignKey : _key) { KeyId = KeyId(_key) };
        var token = new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = spec.Issuer,
            Audience = spec.Audience,
            IssuedAt = issued.UtcDateTime,
            NotBefore = issued.UtcDateTime,
            Expires = issued.UtcDateTime.AddMinutes(60),
            Claims = claims,
            SigningCredentials = new SigningCredentials(signing, SecurityAlgorithms.RsaSha256),
        });
        IdTokens.Add(token);
        return Json(new { token_type = "Bearer", id_token = token, access_token = "stand-in-access-token-not-used" });
    }

    private static string KeyId(RSA key) => Base64UrlEncoder.Encode(SHA256.HashData(key.ExportSubjectPublicKeyInfo()))[..16];

    private static object Jwk(RSA key)
    {
        var parameters = key.ExportParameters(includePrivateParameters: false);
        return new
        {
            kty = "RSA",
            use = "sig",
            alg = "RS256",
            kid = KeyId(key),
            n = Base64UrlEncoder.Encode(parameters.Modulus),
            e = Base64UrlEncoder.Encode(parameters.Exponent),
        };
    }

    private static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };

    protected override void Dispose(bool disposing)
    {
        // The HTTP client factory may dispose handlers; the stand-in lives as long as the test (so the base is told "not disposing").
        base.Dispose(false);
    }
}
