using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shared.Authorization;
using Shared.Testing;

namespace Shared.Authorization.Tests;

/// <summary>
/// Privileges that demand a recently confirmed second factor: the token claim <c>mfa_until</c> (set by the step-up of Access only) is checked
/// against the <see cref="TimeProvider"/>. Only a missing second factor is answered with the problem title <c>SecondFactorRequired</c>.
/// </summary>
public sealed class SecondFactorTests : IAsyncLifetime
{
    private const string Pay = "payslip:read";
    private static readonly Guid Erika = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid Max = Guid.Parse("00000000-0000-0000-0000-00000000000a");

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero));
    private readonly InMemoryPermissionCache _cache = new();
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    /// <summary>Person from <c>X-Test-Employee</c>; the token claim <c>mfa_until</c> from <c>X-Test-Mfa-Until</c>, as Access writes it.</summary>
    private sealed class HeaderAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(TestAuthHandler.EmployeeHeader, out var employee))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim> { new("employee_id", employee.ToString()) };
            if (Request.Headers.TryGetValue("X-Test-Mfa-Until", out var until))
            {
                claims.Add(new Claim(AppClaimTypes.SecondFactorUntilTokenClaim, until.ToString()));
            }

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, "Header")), "Header")));
        }
    }

    public async Task InitializeAsync()
    {
        _cache.Grant(Erika, new TestGrant(Pay, Unrestricted: true), new TestGrant("absence:read-own", Unrestricted: true));
        _cache.Grant(Max, new TestGrant("absence:read-own", Unrestricted: true));

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<TimeProvider>(_time);
        builder.Services.AddSingleton<IPermissionCache>(_cache);
        builder.Services.AddAuthentication("Header").AddScheme<AuthenticationSchemeOptions, HeaderAuthHandler>("Header", _ => { });
        builder.Services.AddSharedAuthorization();
        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();

        _app.MapGet("/payslips", () => "ok").RequireAuthorization(AppPolicies.PrivilegeWithSecondFactor(Pay));
        _app.MapGet("/me/absences", () => "ok").RequireAuthorization(AppPolicies.Privilege("absence:read-own"));
        _app.MapGet("/manual", async (IAuthorizationService auth, HttpContext ctx) =>
        {
            var result = await auth.AuthorizeAsync(ctx.User, null, AppPolicies.PrivilegeWithSecondFactor(Pay));
            return result.Succeeded ? "ok" : result.OnlySecondFactorMissing() ? "second-factor" : "forbidden";
        }).RequireAuthorization();
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private string Until(TimeSpan fromNow) => (_time.GetUtcNow() + fromNow).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);

    private async Task<(HttpStatusCode Status, string Body)> Get(string url, Guid? person, string? mfaUntil = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (person is { } id)
        {
            request.AsUser(id);
        }

        if (mfaUntil is not null)
        {
            request.Headers.Add("X-Test-Mfa-Until", mfaUntil);
        }

        using var response = await _client.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static string? Title(string body) =>
        string.IsNullOrWhiteSpace(body) ? null : JsonDocument.Parse(body).RootElement.TryGetProperty("title", out var t) ? t.GetString() : null;

    [Fact]
    public async Task Privilege_with_a_valid_confirmation_is_allowed() =>
        (await Get("/payslips", Erika, Until(TimeSpan.FromMinutes(10)))).Status.ShouldBe(HttpStatusCode.OK);

    [Fact]
    public async Task Privilege_without_confirmation_is_403_with_the_second_factor_problem()
    {
        var (status, body) = await Get("/payslips", Erika);

        status.ShouldBe(HttpStatusCode.Forbidden);
        Title(body).ShouldBe("SecondFactorRequired");
    }

    [Fact]
    public async Task Confirmation_that_has_run_out_is_asked_for_again()
    {
        var until = Until(TimeSpan.FromMinutes(15));
        (await Get("/payslips", Erika, until)).Status.ShouldBe(HttpStatusCode.OK);

        _time.Advance(TimeSpan.FromMinutes(15)); // exactly the end: no longer valid
        var (status, body) = await Get("/payslips", Erika, until);

        status.ShouldBe(HttpStatusCode.Forbidden);
        Title(body).ShouldBe("SecondFactorRequired");
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData("1.5")]
    [InlineData("0")]
    public async Task Malformed_or_old_claims_count_as_no_confirmation(string claim)
    {
        var (status, body) = await Get("/payslips", Erika, claim);

        status.ShouldBe(HttpStatusCode.Forbidden);
        Title(body).ShouldBe("SecondFactorRequired");
    }

    [Fact]
    public async Task Missing_privilege_stays_a_plain_403_even_with_a_valid_confirmation()
    {
        var withConfirmation = await Get("/payslips", Max, Until(TimeSpan.FromMinutes(10)));
        var without = await Get("/payslips", Max);

        withConfirmation.Status.ShouldBe(HttpStatusCode.Forbidden);
        Title(withConfirmation.Body).ShouldBeNull();
        without.Status.ShouldBe(HttpStatusCode.Forbidden);
        Title(without.Body).ShouldBeNull(); // does not reveal that a second factor could help
    }

    [Fact]
    public async Task Person_without_any_permission_copy_gets_the_plain_403()
    {
        var (status, body) = await Get("/payslips", Guid.NewGuid(), Until(TimeSpan.FromMinutes(10)));

        status.ShouldBe(HttpStatusCode.Forbidden);
        Title(body).ShouldBeNull();
    }

    [Fact]
    public async Task Anonymous_is_unauthorized() =>
        (await Get("/payslips", null)).Status.ShouldBe(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task Ordinary_privileges_do_not_ask_for_a_second_factor() =>
        (await Get("/me/absences", Erika)).Status.ShouldBe(HttpStatusCode.OK);

    [Fact]
    public async Task Handlers_calling_the_authorization_service_can_tell_second_factor_from_privilege()
    {
        (await Get("/manual", Erika)).Body.ShouldBe("second-factor");
        (await Get("/manual", Erika, Until(TimeSpan.FromMinutes(1)))).Body.ShouldBe("ok");
        (await Get("/manual", Max, Until(TimeSpan.FromMinutes(1)))).Body.ShouldBe("forbidden");
        (await Get("/manual", Max)).Body.ShouldBe("forbidden");
    }
}
