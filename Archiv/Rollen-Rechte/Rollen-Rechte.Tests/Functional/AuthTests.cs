using System.Net;
using System.Text;
using Rollen_Rechte.Tests.Infrastructure;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Rollen_Rechte.Tests.Functional;

public class AuthTests(ApiFactory factory) : ApiTestBase(factory)
{
    private const string GenericLoginError = "Benutzername oder Passwort ist falsch.";

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsTokenAndRefreshTokenWithoutEnvelope()
    {
        var user = await CreateAdminAsync();

        var response = await PostAsync("/api/auth/login", new { username = user.Username, password = DefaultPassword });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await JsonAsync(response);
        Assert.False(json.TryGetProperty("success", out _)); // bewusst ohne Envelope
        Assert.False(string.IsNullOrEmpty(json.GetProperty("token").GetString()));
        Assert.False(string.IsNullOrEmpty(json.GetProperty("refresh_token").GetString()));
    }

    [Fact]
    public async Task Login_Token_ContainsUsernameAndExpClaimsForTheFrontend()
    {
        var user = await CreateAdminAsync();
        var token = await LoginAsync(user.Username);

        var jwt = new JsonWebToken(token);

        Assert.Equal(user.Username, jwt.GetClaim("username").Value);
        Assert.Equal(user.Id.ToString(), jwt.GetClaim("sub").Value); // die User-ID, damit Anwendungen ohne eigene Benutzer-Tabelle den User erkennen
        Assert.True(jwt.ValidTo > DateTime.UtcNow.AddDays(29));
        Assert.True(jwt.TryGetPayloadValue<long>("iat", out _));
        Assert.False(jwt.TryGetPayloadValue<string[]>("roles", out _)); // Rechte kommen aus /api/me/privileges, nicht aus dem Token
    }

    [Fact]
    public async Task Login_WithWrongPassword_Returns401InEnvelope()
    {
        var user = await CreateAdminAsync();

        var response = await PostAsync("/api/auth/login", new { username = user.Username, password = "falsch" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var json = await JsonAsync(response);
        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.Equal(GenericLoginError, json.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Login_WithDeactivatedUser_IsRejected()
    {
        var user = await CreateAdminAsync(active: false);

        var response = await PostAsync("/api/auth/login", new { username = user.Username, password = DefaultPassword });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_DeactivatedAccountAndWrongPassword_ShowIdenticalMessage()
    {
        var deactivated = await CreateAdminAsync(active: false);
        var active = await CreateAdminAsync();

        var a = await JsonAsync(await PostAsync("/api/auth/login", new { username = deactivated.Username, password = DefaultPassword }));
        var b = await JsonAsync(await PostAsync("/api/auth/login", new { username = active.Username, password = "falsch" }));
        var c = await JsonAsync(await PostAsync("/api/auth/login", new { username = "gibt-es-nicht", password = "egal" }));

        Assert.Equal(GenericLoginError, a.GetProperty("message").GetString());
        Assert.Equal(a.GetProperty("message").GetString(), b.GetProperty("message").GetString());
        Assert.Equal(a.GetProperty("message").GetString(), c.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Refresh_ReturnsNewTokenPair()
    {
        var user = await CreateAdminAsync();
        var login = await JsonAsync(await PostAsync("/api/auth/login", new { username = user.Username, password = DefaultPassword }));

        var response = await PostAsync("/api/token/refresh",
            new Dictionary<string, string> { ["refresh_token"] = login.GetProperty("refresh_token").GetString()! });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await JsonAsync(response);
        Assert.False(string.IsNullOrEmpty(json.GetProperty("token").GetString()));
        Assert.False(string.IsNullOrEmpty(json.GetProperty("refresh_token").GetString()));
    }

    [Fact]
    public async Task Refresh_WithUnknownToken_Returns401()
    {
        var response = await PostAsync("/api/token/refresh", new Dictionary<string, string> { ["refresh_token"] = "gibt-es-nicht" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_ExtendsRefreshTokenLifetime()
    {
        var user = await CreateAdminAsync();
        var login = await JsonAsync(await PostAsync("/api/auth/login", new { username = user.Username, password = DefaultPassword }));
        var refreshToken = login.GetProperty("refresh_token").GetString()!;
        await Factory.WithDbAsync(async (db, _) =>
        {
            var stored = db.RefreshTokens.Single(t => t.Token == refreshToken);
            stored.Valid = DateTime.UtcNow.AddDays(1);
            await db.SaveChangesAsync();
        });

        await PostAsync("/api/token/refresh", new Dictionary<string, string> { ["refresh_token"] = refreshToken });

        var valid = await QueryDbAsync(db => Task.FromResult(db.RefreshTokens.Single(t => t.Token == refreshToken).Valid));
        Assert.True(valid > DateTime.UtcNow.AddDays(29));
    }

    [Fact]
    public async Task DeactivatingUser_RejectsItsExistingRefreshToken()
    {
        var user = await CreateAdminAsync();
        var login = await JsonAsync(await PostAsync("/api/auth/login", new { username = user.Username, password = DefaultPassword }));
        await Factory.WithDbAsync(async (db, _) =>
        {
            db.Users.Single(u => u.Id == user.Id).Active = false;
            await db.SaveChangesAsync();
        });

        var response = await PostAsync("/api/token/refresh",
            new Dictionary<string, string> { ["refresh_token"] = login.GetProperty("refresh_token").GetString()! });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeactivatingUser_RejectsItsExistingBearerTokenOnTheNextRequest()
    {
        var (user, token) = await AdminWithTokenAsync();
        Assert.Equal(HttpStatusCode.OK, (await GetAsync("/api/users", token)).StatusCode);

        await Factory.WithDbAsync(async (db, _) =>
        {
            db.Users.Single(u => u.Id == user.Id).Active = false;
            await db.SaveChangesAsync();
        });

        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync("/api/users", token)).StatusCode);
    }

    [Fact]
    public async Task ProtectedRoute_WithoutToken_Returns401InEnvelope()
    {
        var response = await GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var json = await JsonAsync(response);
        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.Equal("JWT Token not found", json.GetProperty("message").GetString());
    }

    [Fact]
    public async Task ProtectedRoute_WithGarbageToken_ReturnsInvalidJwtMessage()
    {
        var json = await JsonAsync(await GetAsync("/api/users", "kein.echtes.token"));

        Assert.Equal("Invalid JWT Token", json.GetProperty("message").GetString());
    }

    [Fact]
    public async Task ProtectedRoute_WithExpiredToken_ReturnsExpiredJwtMessage()
    {
        var user = await CreateAdminAsync();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("integration-test-key-0123456789abcdef0123456789"));
        var expired = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Claims = new Dictionary<string, object> { ["username"] = user.Username },
            IssuedAt = DateTime.UtcNow.AddDays(-40),
            NotBefore = DateTime.UtcNow.AddDays(-40),
            Expires = DateTime.UtcNow.AddDays(-10),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        });

        var response = await GetAsync("/api/users", expired);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Expired JWT Token", (await JsonAsync(response)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task UnauthorizedResponse_KeepsCorsHeaderForCrossOriginRequest()
    {
        var response = await SendAsync(HttpMethod.Get, "/api/users", configure: r => r.Headers.Add("Origin", "http://localhost:4200"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("http://localhost:4200", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task Login_IsThrottledAfterFiveAttemptsForTheSameUsername()
    {
        var user = await CreateAdminAsync();
        for (var i = 0; i < 5; i++)
            await PostAsync("/api/auth/login", new { username = user.Username, password = "falsch" });

        // Selbst das richtige Passwort wird jetzt abgewiesen — mit derselben generischen Meldung, nicht mit 429.
        var response = await PostAsync("/api/auth/login", new { username = user.Username, password = DefaultPassword });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(GenericLoginError, (await JsonAsync(response)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task Login_IsNotThrottledTooEarly()
    {
        var user = await CreateAdminAsync();
        for (var i = 0; i < 4; i++)
            await PostAsync("/api/auth/login", new { username = user.Username, password = "falsch" });

        var response = await PostAsync("/api/auth/login", new { username = user.Username, password = DefaultPassword });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_IsThrottledPerIpAcrossDifferentUsernames()
    {
        var user = await CreateAdminAsync();
        for (var i = 0; i < 25; i++)
            await PostAsync("/api/auth/login", new { username = $"nobody-{i}-{Guid.NewGuid():N}", password = "x" });

        var response = await PostAsync("/api/auth/login", new { username = user.Username, password = DefaultPassword });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_ThrottleIsPerIp_AnotherIpIsUnaffected()
    {
        var user = await CreateAdminAsync();
        for (var i = 0; i < 6; i++)
            await PostAsync("/api/auth/login", new { username = user.Username, password = "falsch" });
        UseNewIp();

        var response = await PostAsync("/api/auth/login", new { username = user.Username, password = DefaultPassword });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
