using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Access.Api;
using Access.Contracts;
using Access.Core.Login;
using Access.Infrastructure;
using BcIntegration.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Shared.Infrastructure.Messaging;
using Shared.Messaging;
using Shared.Testing;
using static Access.Api.Tests.AccessApi;

namespace Access.Api.Tests;

/// <summary>Login with personnel number and PIN through the real API: tokens, refresh, lockout, BC outage, privacy of the PIN.</summary>
public sealed class LoginFlowTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IDisposable
{
    private sealed class FakeBc : IBcLoginGateway
    {
        public Dictionary<string, string> Pins { get; } = new() { ["10042"] = "4711", ["10001"] = "1234" };
        public bool Down { get; set; }

        public Task<BcPinResult> VerifyAsync(string personnelNumber, string pin, CancellationToken cancellationToken) =>
            Down ? throw new BcLoginUnavailableException() : Task.FromResult(new BcPinResult(Pins.TryGetValue(personnelNumber, out var p) && p == pin));
    }

    private readonly FakeBc _bc = new();
    private readonly CapturingLoggerProvider _logs = new();

    public void Dispose() => _logs.Dispose();

    private Task<AccessApi> StartApi() => StartAsync(postgres, realAuthentication: true, configure: services =>
    {
        services.AddScoped<IBcLoginGateway>(_ => _bc);
        services.AddLogging(b => b.AddProvider(_logs));
    });

    private static async Task<HttpResponseMessage> Login(AccessApi api, string number, string pin) =>
        await api.Http.PostAsJsonAsync("/api/auth/login", new LoginRequest(number, pin));

    private static async Task<TokenResponse> LoggedIn(AccessApi api, string number = "10042", string pin = "4711")
    {
        using var response = await Login(api, number, pin);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
    }

    private static async Task<HttpResponseMessage> Get(AccessApi api, string url, string? bearer)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (bearer is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        return await api.Http.SendAsync(request);
    }

    [Fact]
    public async Task Valid_login_returns_tokens_and_the_access_token_identifies_the_person()
    {
        await using var api = await StartApi();

        var tokens = await LoggedIn(api);
        using var me = await Get(api, "/api/me/privileges", tokens.AccessToken);

        me.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await me.Content.ReadFromJsonAsync<MyPrivilegesDto>())!.EmployeeId.ShouldBe(Erika);
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(tokens.AccessToken);
        jwt.GetClaim("employee_id").Value.ShouldBe(Erika.ToString());
        jwt.Claims.Select(c => c.Type).Order().ShouldBe(["aud", "employee_id", "exp", "iat", "iss", "nbf", "sub"]); // no roles, no name, no privileges
        jwt.Alg.ShouldBe("RS256");
        tokens.ExpiresAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Missing_or_foreign_tokens_do_not_open_the_api()
    {
        await using var api = await StartApi();
        var tokens = await LoggedIn(api);

        (await Get(api, "/api/me/privileges", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Get(api, "/api/me/privileges", "not.a.token")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Get(api, "/api/me/privileges", tokens.AccessToken[..^3] + "abc")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized); // broken signature
    }

    [Fact]
    public async Task Wrong_pin_and_unknown_person_get_the_same_answer()
    {
        await using var api = await StartApi();

        using var wrong = await Login(api, "10042", "0000");
        using var unknown = await Login(api, "99999", "4711");

        wrong.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        unknown.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await wrong.Content.ReadAsStringAsync()).ShouldBe(await unknown.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Person_who_has_left_cannot_log_in()
    {
        await using var api = await StartApi();
        await api.ReceiveAsync(Erika, "10042", "Erika Mustermann", F017, new DateOnly(2020, 1, 1));

        (await Login(api, "10042", "4711")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Repeated_failures_are_answered_with_429_and_retry_after()
    {
        await using var api = await StartApi();
        for (var i = 0; i < 5; i++)
        {
            (await Login(api, "10042", "0000")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        using var locked = await Login(api, "10042", "4711");

        locked.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        locked.Headers.RetryAfter!.Delta!.Value.TotalSeconds.ShouldBeInRange(1, 15 * 60);
    }

    [Fact]
    public async Task Bc_outage_without_a_stored_pin_is_503_and_with_one_the_login_still_works()
    {
        await using var api = await StartApi();
        _bc.Down = true;
        using var unavailable = await Login(api, "10042", "4711");
        _bc.Down = false;
        await LoggedIn(api);
        _bc.Down = true;

        unavailable.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await Login(api, "10042", "4711")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Login(api, "10042", "9999")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Stored_pin_is_only_hash_and_ciphertext_in_the_database()
    {
        await using var api = await StartApi();
        await LoggedIn(api);

        await using var scope = api.Services.CreateAsyncScope();
        var row = await scope.ServiceProvider.GetRequiredService<AccessDbContext>().Credentials.SingleAsync();

        row.EmployeeId.ShouldBe(Erika);
        row.PinHash.ShouldStartWith("pbkdf2-sha512$");
        row.PinHash.ShouldNotContain("4711");
        row.EncryptedPin.ShouldNotBeNull().ShouldNotContain("4711");
    }

    [Fact]
    public async Task Refresh_rotates_and_a_used_token_ends_all_sessions()
    {
        await using var api = await StartApi();
        var first = await LoggedIn(api);

        using var refreshed = await api.Http.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(first.RefreshToken));
        var second = (await refreshed.Content.ReadFromJsonAsync<TokenResponse>())!;
        using var replay = await api.Http.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(first.RefreshToken));
        using var afterReplay = await api.Http.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(second.RefreshToken));

        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.RefreshToken.ShouldNotBe(first.RefreshToken);
        (await Get(api, "/api/me/privileges", second.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        replay.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        afterReplay.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_is_refused_after_the_person_has_left_and_logout_ends_the_session()
    {
        await using var api = await StartApi();
        var leaver = await LoggedIn(api);
        var stays = await LoggedIn(api, "10001", "1234");
        await api.ReceiveAsync(Erika, "10042", "Erika Mustermann", F017, new DateOnly(2020, 1, 1));

        using var refused = await api.Http.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(leaver.RefreshToken));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", stays.AccessToken);
        using var logout = await api.Http.SendAsync(request);
        using var afterLogout = await api.Http.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(stays.RefreshToken));

        refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        logout.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        afterLogout.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var anonymousLogout = await api.Http.PostAsync("/api/auth/logout", null);
        anonymousLogout.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Erasure_deletes_stored_pin_and_sessions()
    {
        await using var api = await StartApi();
        var tokens = await LoggedIn(api);
        var message = new EmployeeDataErasureRequested(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UnixEpoch, Erika);

        (await api.Services.GetRequiredService<MessageDispatcher>().DispatchAsync(
            new InboundMessage(message.MessageId.ToString(), nameof(EmployeeDataErasureRequested), MessageJson.Serialize(message), 1), CancellationToken.None))
            .Outcome.ShouldBe(DispatchOutcome.Completed);

        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccessDbContext>();
        (await db.Credentials.CountAsync()).ShouldBe(0);
        (await db.RefreshTokens.CountAsync()).ShouldBe(0);
        using var refresh = await api.Http.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(tokens.RefreshToken));
        refresh.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Login(api, "10042", "4711")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_pin_never_appears_in_logs_or_responses()
    {
        // Distinctive PINs (letters and symbols) so that a hit cannot be a coincidence with a number or ID in a log line.
        const string right = "Q7x!pinR";
        const string wrongPin = "W3y!pinX";
        _bc.Pins["10042"] = right;
        await using var api = await StartApi();
        using var wrong = await Login(api, "10042", wrongPin);
        var tokens = await LoggedIn(api, "10042", right);
        _bc.Down = true;
        using var offline = await Login(api, "10042", wrongPin);

        foreach (var body in new[] { await wrong.Content.ReadAsStringAsync(), await offline.Content.ReadAsStringAsync(), JsonSerializer.Serialize(tokens) })
        {
            body.ShouldNotContain(wrongPin);
            body.ShouldNotContain(right);
        }

        _logs.Lines.ShouldNotBeEmpty();
        _logs.Lines.Where(l => l.Contains("pinR", StringComparison.Ordinal) || l.Contains("pinX", StringComparison.Ordinal)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Discovery_and_jwks_publish_the_public_key_only()
    {
        await using var api = await StartApi();

        var discovery = await api.Http.GetFromJsonAsync<JsonElement>("/.well-known/openid-configuration");
        var jwks = await api.Http.GetStringAsync("/.well-known/jwks.json");

        discovery.GetProperty("issuer").GetString().ShouldBe("newworld-access");
        discovery.GetProperty("jwks_uri").GetString().ShouldEndWith("/.well-known/jwks.json");
        jwks.ShouldContain("\"kty\":\"RSA\"");
        foreach (var privatePart in new[] { "\"d\"", "\"p\"", "\"q\"", "\"dp\"", "\"dq\"", "\"qi\"" })
        {
            jwks.ShouldNotContain(privatePart);
        }
    }

    [Fact]
    public async Task Login_endpoints_validate_their_input()
    {
        await using var api = await StartApi();

        (await Login(api, "", "")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var empty = await api.Http.PostAsync("/api/auth/refresh", JsonContent.Create(new RefreshRequest("")));
        empty.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}

public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _lines = new();

    public IReadOnlyCollection<string> Lines => _lines.ToArray();

    public ILogger CreateLogger(string categoryName) => new Logger(_lines);

    public void Dispose()
    {
    }

    private sealed class Logger(System.Collections.Concurrent.ConcurrentQueue<string> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lines.Enqueue(formatter(state, exception));
            if (exception is not null)
            {
                lines.Enqueue(exception.ToString());
            }
        }
    }
}
