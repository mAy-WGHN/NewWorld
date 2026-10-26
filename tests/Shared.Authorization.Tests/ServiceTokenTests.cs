using System.Security.Cryptography;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Shared.Authorization;

namespace Shared.Authorization.Tests;

/// <summary>Service-to-service tokens: signed with the caller's private key (ES256), checked with its public key, short-lived, single use.</summary>
public class ServiceTokenTests
{
    private const string PinRole = "BcIntegration.Pin.Verify";
    private const string ErasureRole = "BcIntegration.EmployeeErasure.Request";

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 26, 9, 0, 0, TimeSpan.Zero));
    private readonly ServiceKeyPair _access = ServiceKeys.Generate();
    private readonly ServiceKeyPair _workforce = ServiceKeys.Generate();

    private static SharedAuthorizationOptions Roles(params string[] roles)
    {
        var options = new SharedAuthorizationOptions();
        foreach (var role in roles)
        {
            options.ServiceRoles.Add(role);
        }

        return options;
    }

    /// <summary>Options of the calling service <paramref name="name"/> (own key, no peers).</summary>
    private static ServiceAuthOptions Caller(string name, ServiceKeyPair key) => new() { Name = name, PrivateKey = key.PrivateKey };

    /// <summary>Options of the BC integration API: accepts "access" with the PIN role, optionally more peers.</summary>
    private ServiceAuthOptions Receiver(Action<ServiceAuthOptions>? configure = null)
    {
        var options = new ServiceAuthOptions { Name = "bc-integration" };
        options.Peers["access"] = new ServicePeerOptions { PublicKeys = [_access.PublicKey], Roles = [PinRole] };
        configure?.Invoke(options);
        return options;
    }

    private ServiceTokenValidator Validator(ServiceAuthOptions? options = null) =>
        new(options ?? Receiver(), Roles(PinRole, ErasureRole), _time);

    private string TokenFromAccess(string audience = "bc-integration") => new ServiceTokenIssuer(Caller("access", _access), _time).Create(audience);

    // --- accepted ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Token_of_a_known_peer_is_accepted_with_exactly_its_granted_roles()
    {
        var result = await Validator().ValidateAsync(TokenFromAccess());

        result.Succeeded.ShouldBeTrue();
        result.Service.ShouldBe("access");
        result.Roles.ShouldBe([PinRole]);
    }

    [Fact]
    public async Task Peer_without_roles_is_authenticated_but_holds_no_role()
    {
        var options = Receiver(o => o.Peers["access"].Roles.Clear());

        var result = await Validator(options).ValidateAsync(TokenFromAccess());

        result.Succeeded.ShouldBeTrue();
        result.Roles.ShouldBeEmpty();
    }

    [Fact]
    public async Task Rotation_both_public_keys_of_a_peer_are_accepted()
    {
        var next = ServiceKeys.Generate();
        var options = Receiver(o => o.Peers["access"].PublicKeys.Add(next.PublicKey));
        var validator = Validator(options);

        var fromOld = await validator.ValidateAsync(TokenFromAccess());
        var fromNew = await validator.ValidateAsync(new ServiceTokenIssuer(Caller("access", next), _time).Create("bc-integration"));

        fromOld.Succeeded.ShouldBeTrue();
        fromNew.Succeeded.ShouldBeTrue();
    }

    // --- rejected ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Token_for_another_service_is_rejected()
    {
        (await Validator().ValidateAsync(TokenFromAccess(audience: "workforce"))).Succeeded.ShouldBeFalse();
    }

    [Fact]
    public async Task Expired_token_is_rejected()
    {
        var token = TokenFromAccess();

        _time.Advance(TimeSpan.FromMinutes(3));

        (await Validator().ValidateAsync(token)).Succeeded.ShouldBeFalse();
    }

    [Fact]
    public async Task Token_signed_with_another_key_is_rejected()
    {
        var token = new ServiceTokenIssuer(Caller("access", _workforce), _time).Create("bc-integration"); // claims to be access, signed by workforce

        (await Validator().ValidateAsync(token)).Succeeded.ShouldBeFalse();
    }

    [Fact]
    public async Task Token_of_an_unknown_service_is_rejected()
    {
        var token = new ServiceTokenIssuer(Caller("workforce", _workforce), _time).Create("bc-integration");

        (await Validator().ValidateAsync(token)).Succeeded.ShouldBeFalse();
    }

    [Fact]
    public async Task Token_cannot_be_used_twice()
    {
        var validator = Validator();
        var token = TokenFromAccess();

        (await validator.ValidateAsync(token)).Succeeded.ShouldBeTrue();
        (await validator.ValidateAsync(token)).Succeeded.ShouldBeFalse();
    }

    [Fact]
    public async Task Token_with_a_long_lifetime_is_rejected()
    {
        var token = Craft(_access, "svc:access", "bc-integration", lifetime: TimeSpan.FromHours(1), SecurityAlgorithms.EcdsaSha256);

        (await Validator().ValidateAsync(token)).Succeeded.ShouldBeFalse();
    }

    [Fact]
    public async Task Token_without_an_id_is_rejected()
    {
        var token = Craft(_access, "svc:access", "bc-integration", lifetime: TimeSpan.FromMinutes(2), SecurityAlgorithms.EcdsaSha256, withId: false);

        (await Validator().ValidateAsync(token)).Succeeded.ShouldBeFalse();
    }

    [Fact]
    public async Task Symmetric_token_is_rejected_even_with_a_known_issuer()
    {
        var key = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "svc:access",
            Audience = "bc-integration",
            Expires = _time.GetUtcNow().UtcDateTime.AddMinutes(2),
            Claims = new Dictionary<string, object> { ["jti"] = Guid.NewGuid().ToString("N") },
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        });

        (await Validator().ValidateAsync(token)).Succeeded.ShouldBeFalse();
    }

    [Fact]
    public async Task Unsigned_token_is_rejected()
    {
        var header = Base64Url("""{"alg":"none","typ":"JWT"}""");
        var payload = Base64Url($$"""{"iss":"svc:access","aud":"bc-integration","jti":"x","nbf":{{_time.GetUtcNow().ToUnixTimeSeconds()}},"exp":{{_time.GetUtcNow().AddMinutes(2).ToUnixTimeSeconds()}}}""");

        (await Validator().ValidateAsync($"{header}.{payload}.")).Succeeded.ShouldBeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-token")]
    [InlineData("a.b.c")]
    public async Task Garbage_is_rejected(string token)
    {
        (await Validator().ValidateAsync(token)).Succeeded.ShouldBeFalse();
    }

    // --- recognising service tokens (so that person tokens go to their own scheme) ----------------------------------

    [Fact]
    public void Service_tokens_are_recognised_by_their_issuer_prefix()
    {
        var validator = Validator();

        validator.IsServiceToken("Bearer " + TokenFromAccess()).ShouldBeTrue();
        validator.IsServiceToken("Bearer " + new ServiceTokenIssuer(Caller("workforce", _workforce), _time).Create("x")).ShouldBeTrue();
        validator.IsServiceToken("Bearer " + Craft(_access, "newworld-access", "newworld", TimeSpan.FromMinutes(5), SecurityAlgorithms.EcdsaSha256)).ShouldBeFalse();
        validator.IsServiceToken("Bearer garbage").ShouldBeFalse();
        validator.IsServiceToken(null).ShouldBeFalse();
        validator.IsServiceToken("Basic abc").ShouldBeFalse();
    }

    // --- issuing -------------------------------------------------------------------------------------------------

    [Fact]
    public void Issued_tokens_are_short_lived_signed_with_es256_and_unique()
    {
        var issuer = new ServiceTokenIssuer(Caller("access", _access), _time);

        var first = new JsonWebToken(issuer.Create("bc-integration"));
        var second = new JsonWebToken(issuer.Create("bc-integration"));

        first.Issuer.ShouldBe("svc:access");
        first.Audiences.ShouldBe(["bc-integration"]);
        first.Alg.ShouldBe("ES256");
        (first.ValidTo - first.ValidFrom).ShouldBeLessThanOrEqualTo(TimeSpan.FromMinutes(5));
        first.Id.ShouldNotBe(second.Id);
    }

    [Fact]
    public void Issuing_without_a_private_key_fails_with_a_clear_message()
    {
        var issuer = new ServiceTokenIssuer(new ServiceAuthOptions { Name = "access" }, _time);

        Should.Throw<InvalidOperationException>(() => issuer.Create("bc-integration")).Message.ShouldContain("PrivateKey");
    }

    [Fact]
    public void Issuing_without_a_name_fails_with_a_clear_message()
    {
        var issuer = new ServiceTokenIssuer(new ServiceAuthOptions { PrivateKey = _access.PrivateKey }, _time);

        Should.Throw<InvalidOperationException>(() => issuer.Create("bc-integration")).Message.ShouldContain("Name");
    }

    // --- configuration is checked at start -------------------------------------------------------------------------

    [Fact]
    public void Granting_a_role_the_api_does_not_know_is_a_configuration_error()
    {
        var options = Receiver(o => o.Peers["access"].Roles.Add("Made.Up.Role"));

        Should.Throw<InvalidOperationException>(() => Validator(options)).Message.ShouldContain("Made.Up.Role");
    }

    [Fact]
    public void A_peer_without_a_public_key_is_a_configuration_error()
    {
        var options = Receiver(o => o.Peers["access"].PublicKeys.Clear());

        Should.Throw<InvalidOperationException>(() => Validator(options));
    }

    [Fact]
    public void A_public_key_that_cannot_be_read_is_a_configuration_error_and_is_not_echoed()
    {
        var options = Receiver(o => o.Peers["access"].PublicKeys[0] = "definitely-not-a-key");

        var ex = Should.Throw<InvalidOperationException>(() => Validator(options));
        ex.Message.ShouldNotContain("definitely-not-a-key");
    }

    [Fact]
    public void Accepting_peers_requires_the_own_name()
    {
        var options = Receiver(o => o.Name = null);

        Should.Throw<InvalidOperationException>(() => Validator(options)).Message.ShouldContain("Name");
    }

    [Fact]
    public void A_private_key_that_cannot_be_read_is_a_configuration_error_and_is_not_echoed()
    {
        var issuer = new ServiceTokenIssuer(new ServiceAuthOptions { Name = "access", PrivateKey = "nonsense-key" }, _time);

        var ex = Should.Throw<InvalidOperationException>(() => issuer.Create("bc-integration"));
        ex.Message.ShouldNotContain("nonsense-key");
    }

    [Fact]
    public void Generated_keys_are_distinct_and_usable()
    {
        var a = ServiceKeys.Generate();
        var b = ServiceKeys.Generate();

        a.PrivateKey.ShouldNotBe(b.PrivateKey);
        a.PublicKey.ShouldNotBe(b.PublicKey);
        a.PrivateKey.ShouldNotBe(a.PublicKey);
    }

    // --- helpers -------------------------------------------------------------------------------------------------

    private string Craft(ServiceKeyPair key, string issuer, string audience, TimeSpan lifetime, string algorithm, bool withId = true)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(Convert.FromBase64String(key.PrivateKey), out _);
        var now = _time.GetUtcNow().UtcDateTime;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now + lifetime,
            Claims = withId ? new Dictionary<string, object> { ["jti"] = Guid.NewGuid().ToString("N") } : null,
            SigningCredentials = new SigningCredentials(new ECDsaSecurityKey(ecdsa) { CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false } }, algorithm),
        });
    }

    private static string Base64Url(string json) => Base64UrlEncoder.Encode(json);
}
