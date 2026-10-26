using System.Net;
using System.Net.Http.Headers;
using Shared.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Authorization;

namespace Shared.Authorization.Tests;

/// <summary>Service tokens end to end through ASP.NET Core: the real scheme, the policies and the outgoing handler.</summary>
public sealed class ServiceAuthenticationTests : IAsyncLifetime
{
    private const string PinRole = "BcIntegration.Pin.Verify";
    private const string ErasureRole = "BcIntegration.EmployeeErasure.Request";

    private readonly ServiceKeyPair _access = ServiceKeys.Generate();
    private readonly ServiceKeyPair _workforce = ServiceKeys.Generate();
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        // The receiving API ("bc-integration"): access may verify PINs, workforce is known but holds no role.
        var settings = new Dictionary<string, string?>
        {
            ["ServiceAuth:Name"] = "bc-integration",
            ["ServiceAuth:Peers:access:PublicKeys:0"] = _access.PublicKey,
            ["ServiceAuth:Peers:access:Roles:0"] = PinRole,
            ["ServiceAuth:Peers:workforce:PublicKeys:0"] = _workforce.PublicKey,
        };
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddSharedAuthorization(o =>
        {
            o.ServiceRoles.Add(PinRole);
            o.ServiceRoles.Add(ErasureRole);
        });
        builder.Services.AddServiceAuthentication(builder.Configuration);
        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapPost("/internal/verify-pin", (HttpContext ctx) => ctx.User.FindFirst(AppClaimTypes.ServiceName)!.Value)
            .RequireAuthorization(AppPolicies.ServiceRole(PinRole));
        _app.MapPost("/internal/erasure", () => "ok").RequireAuthorization(AppPolicies.ServiceRole(ErasureRole));
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private static ServiceTokenIssuer Issuer(string name, ServiceKeyPair key) =>
        new(new ServiceAuthOptions { Name = name, PrivateKey = key.PrivateKey }, TimeProvider.System);

    private async Task<HttpResponseMessage> Post(string url, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task Peer_with_the_role_may_call_the_endpoint_and_is_identified_by_name()
    {
        using var response = await Post("/internal/verify-pin", Issuer("access", _access).Create("bc-integration"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("access");
    }

    [Fact]
    public async Task Peer_without_the_role_is_forbidden()
    {
        using var response = await Post("/internal/verify-pin", Issuer("workforce", _workforce).Create("bc-integration"));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Peer_cannot_use_a_role_that_was_not_granted_to_it()
    {
        using var response = await Post("/internal/erasure", Issuer("access", _access).Create("bc-integration"));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task No_token_is_unauthorized()
    {
        using var response = await Post("/internal/verify-pin", null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Forged_token_of_a_known_peer_is_unauthorized()
    {
        using var response = await Post("/internal/verify-pin", Issuer("access", _workforce).Create("bc-integration"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Replayed_token_is_unauthorized()
    {
        var token = Issuer("access", _access).Create("bc-integration");

        (await Post("/internal/verify-pin", token)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Post("/internal/verify-pin", token)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Token_meant_for_another_service_is_unauthorized()
    {
        using var response = await Post("/internal/verify-pin", Issuer("access", _access).Create("workforce"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Outgoing_handler_signs_every_call_with_a_new_token_that_the_receiver_accepts()
    {
        var capture = new CapturingHandler();
        using var handler = new ServiceTokenHandler(Issuer("access", _access), "bc-integration") { InnerHandler = capture };
        using var caller = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };

        await caller.PostAsync("/x", null);
        await caller.PostAsync("/x", null);

        capture.Tokens.Count.ShouldBe(2);
        capture.Tokens[0].ShouldNotBe(capture.Tokens[1]);
        foreach (var token in capture.Tokens)
        {
            (await Post("/internal/verify-pin", token)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Host_does_not_start_with_a_role_that_the_api_does_not_know()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ServiceAuth:Name"] = "bc-integration",
            ["ServiceAuth:Peers:access:PublicKeys:0"] = _access.PublicKey,
            ["ServiceAuth:Peers:access:Roles:0"] = "Made.Up.Role",
        });
        builder.Services.AddSharedAuthorization(o => o.ServiceRoles.Add(PinRole));
        builder.Services.AddServiceAuthentication(builder.Configuration);
        await using var app = builder.Build();

        await Should.ThrowAsync<InvalidOperationException>(() => app.StartAsync());
    }

    [Fact]
    public async Task Without_peers_no_service_can_authenticate()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSharedAuthorization(o => o.ServiceRoles.Add(PinRole));
        builder.Services.AddTestAuthentication();
        builder.Services.AddServiceAuthentication(builder.Configuration, TestAuthHandler.SchemeName);
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapPost("/internal/verify-pin", () => "ok").RequireAuthorization(AppPolicies.ServiceRole(PinRole));
        await app.StartAsync();
        using var client = app.GetTestClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/verify-pin");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Issuer("access", _access).Create("bc-integration"));
        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<string> Tokens { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Tokens.Add(request.Headers.Authorization!.Parameter!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
