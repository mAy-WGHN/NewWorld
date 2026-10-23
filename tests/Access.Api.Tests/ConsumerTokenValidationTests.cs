using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Access.Api;
using Access.Core.Login;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Shared.Authorization;
using Shared.Testing;
using static Access.Api.Tests.AccessApi;

namespace Access.Api.Tests;

/// <summary>
/// Another API (as Workforce will be) accepts tokens issued by Access: it learns the public key through discovery + JWKS once and
/// validates every token locally. Together with its own permission copy it decides without asking Access.
/// </summary>
public sealed class ConsumerTokenValidationTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private sealed class AcceptingBc : IBcLoginGateway
    {
        public Task<BcPinResult> VerifyAsync(string personnelNumber, string pin, CancellationToken cancellationToken) => Task.FromResult(new BcPinResult(true));
    }

    private static async Task<WebApplication> StartConsumerAsync(AccessApi access, InMemoryPermissionCache cache)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Authority"] = "https://access.local",
            ["Authentication:Audience"] = "newworld",
        });
        builder.Services.AddSingleton<IPermissionCache>(cache);
        builder.Services.AddSharedJwtAuthentication(builder.Configuration);
        builder.Services.AddSharedAuthorization();
        builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.BackchannelHttpHandler = access.Server.CreateHandler(); // discovery and JWKS come from Access
        });
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/me/absences", () => "ok").RequireAuthorization(AppPolicies.Privilege("absence:read-own"));
        await app.StartAsync();
        return app;
    }

    private static async Task<HttpStatusCode> Call(WebApplication consumer, string? token)
    {
        using var client = consumer.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/me/absences");
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    private async Task<(AccessApi Access, string Token)> LoginAsync()
    {
        var access = await StartAsync(postgres, realAuthentication: true, configure: s => s.AddScoped<IBcLoginGateway, AcceptingBc>());
        using var response = await access.Http.PostAsJsonAsync("/api/auth/login", new LoginRequest("10042", "any"));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (access, (await response.Content.ReadFromJsonAsync<TokenResponse>())!.AccessToken);
    }

    [Fact]
    public async Task Consumer_accepts_the_token_and_decides_from_its_own_copy()
    {
        var (access, token) = await LoginAsync();
        await using var _ = access;
        var cache = new InMemoryPermissionCache();
        await using var consumer = await StartConsumerAsync(access, cache);

        (await Call(consumer, token)).ShouldBe(HttpStatusCode.Forbidden); // authenticated, but no privilege in the copy yet
        cache.Grant(Erika, new TestGrant("absence:read-own"));
        (await Call(consumer, token)).ShouldBe(HttpStatusCode.OK);
        cache.Grant(Erika); // privilege withdrawn by a later PermissionsChanged
        (await Call(consumer, token)).ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Consumer_rejects_tokens_from_other_issuers_and_keys_and_wrong_audiences()
    {
        var (access, token) = await LoginAsync();
        await using var _ = access;
        var cache = new InMemoryPermissionCache();
        cache.Grant(Erika, new TestGrant("absence:read-own"));
        await using var consumer = await StartConsumerAsync(access, cache);

        string Forge(string issuer, string audience, RSA key) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Expires = DateTime.UtcNow.AddMinutes(5),
            Claims = new Dictionary<string, object> { ["employee_id"] = Erika.ToString() },
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(key), SecurityAlgorithms.RsaSha256),
        });

        using var attacker = RSA.Create(2048);
        (await Call(consumer, token)).ShouldBe(HttpStatusCode.OK);
        (await Call(consumer, Forge("newworld-access", "newworld", attacker))).ShouldBe(HttpStatusCode.Unauthorized);  // right claims, wrong key
        (await Call(consumer, Forge("someone-else", "newworld", attacker))).ShouldBe(HttpStatusCode.Unauthorized);     // wrong issuer
        (await Call(consumer, token[..^4] + "AAAA")).ShouldBe(HttpStatusCode.Unauthorized);                            // broken signature
        (await Call(consumer, null)).ShouldBe(HttpStatusCode.Unauthorized);
    }
}
