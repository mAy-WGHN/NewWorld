using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Access.Api;
using Access.Core.Login;
using Access.Core.StepUp;
using Access.Infrastructure;
using Access.Infrastructure.StepUp;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Shared.Authorization;
using Shared.Testing;
using static Access.Api.Tests.AccessApi;

namespace Access.Api.Tests;

/// <summary>
/// The second factor with Entra through the real API against a stand-in for the tenant (<see cref="EntraStandIn"/>): the start, the callback with
/// all its checks, the token with the claim <c>mfa_until</c> and the use of it by another API. Time runs on a fake clock.
/// </summary>
public sealed class StepUpFlowTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string ErikaUpn = "erika.mustermann@contoso.test";
    private static readonly Guid ErikaOid = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000e1");
    private static readonly Guid OtherOid = Guid.Parse("bbbbbbbb-0000-0000-0000-0000000000b2");

    private static EntraStandIn.Spec Regular(Guid? oid = null, string upn = ErikaUpn) => new() { Oid = oid ?? ErikaOid, Upn = upn };

    private sealed class AcceptingBc : IBcLoginGateway
    {
        public Task<BcPinResult> VerifyAsync(string personnelNumber, string pin, CancellationToken cancellationToken) => Task.FromResult(new BcPinResult(true));
    }

    private sealed class Scenario : IAsyncDisposable
    {
        public required AccessApi Api { get; init; }
        public required EntraStandIn Entra { get; init; }
        public required FakeTimeProvider Time { get; init; }
        public required CapturingLoggerProvider Logs { get; init; }
        public required HttpClient NoRedirect { get; init; }

        public async ValueTask DisposeAsync()
        {
            NoRedirect.Dispose();
            await Api.DisposeAsync();
            Logs.Dispose();
        }
    }

    private async Task<Scenario> StartScenarioAsync(bool enabled = true, bool realAuthentication = false)
    {
        var real = TimeProvider.System.GetUtcNow();
        var time = new FakeTimeProvider(new DateTimeOffset(real.Ticks - real.Ticks % TimeSpan.TicksPerSecond, TimeSpan.Zero)); // whole seconds: the database keeps microseconds
        var entra = new EntraStandIn(() => time.GetUtcNow());
        var logs = new CapturingLoggerProvider();
        var api = await StartAsync(postgres, realAuthentication, services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(time);
            services.AddLogging(b => b.AddProvider(logs));
            services.AddScoped<IBcLoginGateway, AcceptingBc>();
            services.AddSingleton(enabled
                ? new StepUpOptions
                {
                    Enabled = true,
                    TenantId = EntraStandIn.TenantId,
                    ClientId = EntraStandIn.ClientId,
                    ClientSecret = EntraStandIn.ClientSecret,
                    RedirectUri = EntraStandIn.RedirectUri,
                    ReturnUrl = "https://portal.test/step-up",
                    Authority = EntraStandIn.Authority,
                }
                : new StepUpOptions());
            services.AddHttpClient(EntraOidcClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => entra);
        });
        return new Scenario
        {
            Api = api,
            Entra = entra,
            Time = time,
            Logs = logs,
            NoRedirect = api.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }),
        };
    }

    private static Func<HttpRequestMessage, HttpRequestMessage> As(Guid person) => r => r.AsUser(person);

    private static async Task LinkAsync(Scenario s, Guid person = default, string upn = ErikaUpn)
    {
        using var response = await s.Api.SendAsync(HttpMethod.Put, $"/api/people/{(person == default ? Erika : person)}/entra-link", As(Admin), new SetEntraLinkRequest(upn));
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }

    private static async Task<string> StartUrlAsync(Scenario s, Guid person = default)
    {
        using var response = await s.Api.SendAsync(HttpMethod.Post, "/api/auth/step-up/start", As(person == default ? Erika : person));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<StepUpStartResponse>())!.AuthorizationUrl;
    }

    /// <summary>What the browser gets from the callback: the status and the <c>result</c> of the redirect (<c>null</c> without one).</summary>
    private static async Task<(HttpStatusCode Status, string? Result, string? Location)> CallbackAsync(Scenario s, string query)
    {
        using var response = await s.NoRedirect.GetAsync($"/api/auth/step-up/callback{query}");
        var location = response.Headers.Location?.ToString();
        var result = location is null ? null : QueryHelpers.ParseQuery(new Uri(location, UriKind.RelativeOrAbsolute).Query)["result"].ToString();
        return (response.StatusCode, result, location);
    }

    /// <summary>Start, sign in at the stand-in with <paramref name="spec"/>, come back: returns <c>ok</c> or <c>failed</c>.</summary>
    private static async Task<string?> ConfirmAsync(Scenario s, EntraStandIn.Spec spec, Guid person = default)
    {
        var url = await StartUrlAsync(s, person);
        var (code, state) = s.Entra.SignIn(url, spec);
        var (status, result, _) = await CallbackAsync(s, $"?code={code}&state={state}");
        status.ShouldBe(HttpStatusCode.Redirect);
        return result;
    }

    private static async Task<T> InDbAsync<T>(Scenario s, Func<AccessDbContext, Task<T>> read)
    {
        await using var scope = s.Api.Services.CreateAsyncScope();
        return await read(scope.ServiceProvider.GetRequiredService<AccessDbContext>());
    }

    private static async Task<HttpResponseMessage> StepUpTokenAsync(Scenario s, Guid person = default) =>
        await s.Api.SendAsync(HttpMethod.Post, "/api/auth/step-up/token", As(person == default ? Erika : person));

    private static async Task<string?> ProblemTitle(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("title").GetString();

    // ---------------------------------------------------------------- start

    [Fact]
    public async Task Start_returns_the_sign_in_address_with_pkce_state_nonce_and_forced_login_but_never_the_secret()
    {
        await using var s = await StartScenarioAsync();
        await LinkAsync(s);

        var url = await StartUrlAsync(s);

        var uri = new Uri(url);
        uri.GetLeftPart(UriPartial.Path).ShouldBe($"https://login.entra.test/{EntraStandIn.TenantId}/oauth2/v2.0/authorize");
        var query = QueryHelpers.ParseQuery(uri.Query);
        query["client_id"].ToString().ShouldBe(EntraStandIn.ClientId);
        query["response_type"].ToString().ShouldBe("code");
        query["redirect_uri"].ToString().ShouldBe(EntraStandIn.RedirectUri);
        query["scope"].ToString().ShouldBe("openid");
        query["prompt"].ToString().ShouldBe("login");
        query["code_challenge_method"].ToString().ShouldBe("S256");
        query["code_challenge"].ToString().Length.ShouldBe(43);
        query["state"].ToString().Length.ShouldBeGreaterThan(20);
        query["nonce"].ToString().Length.ShouldBeGreaterThan(20);
        url.ShouldNotContain(EntraStandIn.ClientSecret);
        (await StartUrlAsync(s)).ShouldNotBe(url); // new state, nonce and verifier each time
    }

    [Fact]
    public async Task Start_without_a_link_is_404_NoEntraLink()
    {
        await using var s = await StartScenarioAsync();

        using var response = await s.Api.SendAsync(HttpMethod.Post, "/api/auth/step-up/start", As(Erika));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ProblemTitle(response)).ShouldBe("NoEntraLink");
    }

    [Fact]
    public async Task Start_when_the_step_up_is_switched_off_is_503_StepUpUnavailable()
    {
        await using var s = await StartScenarioAsync(enabled: false);
        await LinkAsync(s);

        using var response = await s.Api.SendAsync(HttpMethod.Post, "/api/auth/step-up/start", As(Erika));
        var callback = await CallbackAsync(s, "?code=x&state=y");

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await ProblemTitle(response)).ShouldBe("StepUpUnavailable");
        callback.Status.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Start_when_entra_cannot_be_reached_is_503_StepUpUnavailable()
    {
        await using var s = await StartScenarioAsync();
        await LinkAsync(s);
        s.Entra.Down = true;

        using var response = await s.Api.SendAsync(HttpMethod.Post, "/api/auth/step-up/start", As(Erika));

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await ProblemTitle(response)).ShouldBe("StepUpUnavailable");
    }

    [Fact]
    public async Task Start_and_token_need_a_signed_in_person()
    {
        await using var s = await StartScenarioAsync();

        (await s.Api.SendAsync(HttpMethod.Post, "/api/auth/step-up/start", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await s.Api.SendAsync(HttpMethod.Post, "/api/auth/step-up/token", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await s.Api.SendAsync(HttpMethod.Post, "/api/auth/step-up/start", r => r.AsService(AccessRoles.PermissionsRead))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await s.Api.SendAsync(HttpMethod.Post, "/api/auth/step-up/token", r => r.AsService(AccessRoles.PermissionsRead))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ------------------------------------------------------------- callback

    [Fact]
    public async Task Success_binds_the_account_at_the_first_step_up_and_recognises_it_by_the_object_id_afterwards()
    {
        await using var s = await StartScenarioAsync();
        await LinkAsync(s);

        var first = await ConfirmAsync(s, Regular());
        var link = await s.Api.GetAsync<EntraLinkDto>($"/api/people/{Erika}/entra-link", As(Admin));
        // The user name changed in Microsoft 365 (not yet in the link): the bound object id is what counts now.
        var second = await ConfirmAsync(s, Regular(upn: "erika.neuer-name@contoso.test"));

        first.ShouldBe("ok");
        link.ShouldBe(new EntraLinkDto(ErikaUpn, true, link.BoundAt));
        link.BoundAt!.Value.ShouldBe(s.Time.GetUtcNow(), TimeSpan.FromSeconds(1));
        second.ShouldBe("ok");
        (await InDbAsync(s, db => db.EntraLinks.SingleAsync())).EntraObjectId.ShouldBe(ErikaOid);
    }

    [Fact]
    public async Task Success_redirects_to_the_return_address_with_only_the_result_and_stores_the_confirmation()
    {
        await using var s = await StartScenarioAsync();
        await LinkAsync(s);
        var url = await StartUrlAsync(s);
        var (code, state) = s.Entra.SignIn(url, Regular());

        var (status, result, location) = await CallbackAsync(s, $"?code={code}&state={state}");

        status.ShouldBe(HttpStatusCode.Redirect);
        location.ShouldBe("https://portal.test/step-up?result=ok");
        result.ShouldBe("ok");
        var confirmation = await InDbAsync(s, db => db.StepUpConfirmations.SingleAsync());
        confirmation.EmployeeId.ShouldBe(Erika);
        confirmation.ConfirmedAt.ShouldBe(s.Time.GetUtcNow());
        confirmation.ValidUntil.ShouldBe(s.Time.GetUtcNow() + TimeSpan.FromMinutes(15));
        (await InDbAsync(s, db => db.StepUpRequests.CountAsync())).ShouldBe(0); // the request is used up
    }

    [Fact]
    public async Task The_upn_is_compared_without_regard_to_case_and_the_confirmation_lasts_as_configured()
    {
        await using var s = await StartScenarioAsync();
        await LinkAsync(s, upn: "Erika.Mustermann@Contoso.Test");

        (await ConfirmAsync(s, Regular(upn: "erika.mustermann@contoso.TEST"))).ShouldBe("ok");
    }

    public static TheoryData<string, EntraStandIn.Spec> RefusedTokens => new()
    {
        { "no mfa in amr", Regular() with { Amr = ["pwd"] } },
        { "no amr at all", Regular() with { Amr = [] } },
        { "wrong nonce", Regular() with { Nonce = "another-nonce" } },
        { "wrong issuer", Regular() with { Issuer = "https://login.evil.test/" + EntraStandIn.TenantId + "/v2.0" } },
        { "wrong audience", Regular() with { Audience = "99999999-9999-9999-9999-999999999999" } },
        { "wrong tenant", Regular() with { Tenant = "99999999-9999-9999-9999-999999999999" } },
        { "expired", Regular() with { Age = TimeSpan.FromHours(2) } },
        { "signed with another key", Regular() with { ForeignKey = true } },
        { "no user name", Regular(upn: "") },
        { "no object id", Regular(Guid.Empty) },
    };

    [Theory]
    [MemberData(nameof(RefusedTokens))]
    public async Task A_refused_id_token_gives_failed_and_no_confirmation(string reason, EntraStandIn.Spec spec)
    {
        await using var s = await StartScenarioAsync();
        await LinkAsync(s);

        var result = await ConfirmAsync(s, spec);

        result.ShouldBe("failed", reason);
        (await InDbAsync(s, db => db.StepUpConfirmations.CountAsync())).ShouldBe(0);
        (await InDbAsync(s, db => db.EntraLinks.SingleAsync())).EntraObjectId.ShouldBeNull(); // nothing is bound by a refused token
        using var token = await StepUpTokenAsync(s);
        token.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_state_works_once_and_the_second_use_fails()
    {
        await using var s = await StartScenarioAsync();
        await LinkAsync(s);
        var url = await StartUrlAsync(s);
        var (code, state) = s.Entra.SignIn(url, Regular());
        var (secondCode, _) = s.Entra.SignIn(url, Regular()); // a second code for the same state

        var first = await CallbackAsync(s, $"?code={code}&state={state}");
        await using (var scope = s.Api.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AccessDbContext>().StepUpConfirmations.ExecuteDeleteAsync();
        }

        var second = await CallbackAsync(s, $"?code={secondCode}&state={state}");

        first.Result.ShouldBe("ok");
        second.Result.ShouldBe("failed");
        (await InDbAsync(s, db => db.StepUpConfirmations.CountAsync())).ShouldBe(0); // the replay confirmed nothing
    }

    [Fact]
    public async Task A_state_that_ran_out_fails()
    {
        await using var s = await StartScenarioAsync();
        await LinkAsync(s);
        var url = await StartUrlAsync(s);
        var (code, state) = s.Entra.SignIn(url, Regular());

        s.Time.Advance(StepUpService.RequestLifetime + TimeSpan.FromSeconds(1));
        var result = await CallbackAsync(s, $"?code={code}&state={state}");

        result.Result.ShouldBe("failed");
        (await InDbAsync(s, db => db.StepUpConfirmations.CountAsync())).ShouldBe(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?code=abc")]
    [InlineData("?state=unknown&code=abc")]
    [InlineData("?error=access_denied&error_description=nope")]
    public async Task Missing_or_unknown_state_and_provider_errors_fail_without_details(string query)
    {
        await using var s = await StartScenarioAsync();
        await LinkAsync(s);

        var (status, result, location) = await CallbackAsync(s, query);

        status.ShouldBe(HttpStatusCode.Redirect);
        location.ShouldBe("https://portal.test/step-up?result=failed");
        result.ShouldBe("failed");
    }

    [Fact]
    public async Task The_provider_reporting_an_error_for_a_valid_state_fails_and_uses_up_the_state()
    {
        await using var s = await StartScenarioAsync();
        await LinkAsync(s);
        var url = await StartUrlAsync(s);
        var state = QueryHelpers.ParseQuery(new Uri(url).Query)["state"].ToString();

        (await CallbackAsync(s, $"?error=access_denied&state={state}")).Result.ShouldBe("failed");
        (await InDbAsync(s, db => db.StepUpRequests.CountAsync())).ShouldBe(0);
    }

    [Fact]
    public async Task A_code_the_provider_refuses_fails()
    {
        await using var s = await StartScenarioAsync();
        await LinkAsync(s);
        var state = QueryHelpers.ParseQuery(new Uri(await StartUrlAsync(s)).Query)["state"].ToString();

        (await CallbackAsync(s, $"?code=forged-code&state={state}")).Result.ShouldBe("failed");
    }

    [Fact]
    public async Task The_provider_being_down_at_the_callback_fails()
    {
        await using var s = await StartScenarioAsync();
        await LinkAsync(s);
        var url = await StartUrlAsync(s);
        var (code, state) = s.Entra.SignIn(url, Regular());
        s.Entra.Down = true;

        (await CallbackAsync(s, $"?code={code}&state={state}")).Result.ShouldBe("failed");
    }

    [Fact]
    public async Task Another_object_id_for_a_bound_person_is_refused_even_with_the_right_user_name()
    {
        await using var s = await StartScenarioAsync();
        await LinkAsync(s);
        (await ConfirmAsync(s, Regular())).ShouldBe("ok");
        await using (var scope = s.Api.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AccessDbContext>().StepUpConfirmations.ExecuteDeleteAsync();
        }

        var result = await ConfirmAsync(s, Regular(OtherOid));

        result.ShouldBe("failed");
        (await InDbAsync(s, db => db.StepUpConfirmations.CountAsync())).ShouldBe(0);
        (await InDbAsync(s, db => db.EntraLinks.SingleAsync())).EntraObjectId.ShouldBe(ErikaOid);
    }

    [Fact]
    public async Task A_user_name_that_is_not_the_one_hr_entered_is_refused_for_an_unbound_person()
    {
        await using var s = await StartScenarioAsync();
        await LinkAsync(s);

        var result = await ConfirmAsync(s, Regular(OtherOid, "someone.else@contoso.test"));

        result.ShouldBe("failed");
        (await InDbAsync(s, db => db.EntraLinks.SingleAsync())).EntraObjectId.ShouldBeNull();
    }

    [Fact]
    public async Task The_same_account_cannot_be_bound_to_a_second_person()
    {
        await using var s = await StartScenarioAsync();
        await LinkAsync(s);
        await LinkAsync(s, Max, "max.muster@contoso.test");
        (await ConfirmAsync(s, Regular())).ShouldBe("ok");

        // Max's link is entered with Erika's account at the provider: Entra says oid = Erika's, user name = Max's entry.
        var result = await ConfirmAsync(s, Regular(ErikaOid, "max.muster@contoso.test"), Max);

        result.ShouldBe("failed");
        (await InDbAsync(s, db => db.StepUpConfirmations.AnyAsync(c => c.EmployeeId == Max))).ShouldBeFalse();
    }

    [Fact]
    public async Task Removing_the_link_while_the_person_is_at_the_provider_fails_the_return()
    {
        await using var s = await StartScenarioAsync();
        await LinkAsync(s);
        var url = await StartUrlAsync(s);
        var (code, state) = s.Entra.SignIn(url, Regular());
        using var removed = await s.Api.SendAsync(HttpMethod.Delete, $"/api/people/{Erika}/entra-link", As(Admin));

        removed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await CallbackAsync(s, $"?code={code}&state={state}")).Result.ShouldBe("failed"); // removing the link also removes pending requests
    }

    // ---------------------------------------------------------------- token

    [Fact]
    public async Task Token_needs_a_valid_confirmation_and_the_confirmation_runs_out()
    {
        await using var s = await StartScenarioAsync();
        await LinkAsync(s);

        using var without = await StepUpTokenAsync(s);
        (await ConfirmAsync(s, Regular())).ShouldBe("ok");
        using var with = await StepUpTokenAsync(s);
        s.Time.Advance(TimeSpan.FromMinutes(14));
        using var almost = await StepUpTokenAsync(s);
        s.Time.Advance(TimeSpan.FromMinutes(1));
        using var after = await StepUpTokenAsync(s);

        without.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ProblemTitle(without)).ShouldBe("SecondFactorRequired");
        with.StatusCode.ShouldBe(HttpStatusCode.OK);
        almost.StatusCode.ShouldBe(HttpStatusCode.OK);
        after.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ProblemTitle(after)).ShouldBe("SecondFactorRequired");
    }

    [Fact]
    public async Task A_confirmation_belongs_to_the_person_who_gave_it()
    {
        await using var s = await StartScenarioAsync();
        await LinkAsync(s);
        (await ConfirmAsync(s, Regular())).ShouldBe("ok");

        using var other = await StepUpTokenAsync(s, Max);

        other.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task The_validity_is_taken_from_the_configuration()
    {
        await using var s = await StartScenarioAsync();
        await LinkAsync(s);
        s.Api.Services.GetRequiredService<StepUpOptions>().Validity = TimeSpan.FromMinutes(5);

        (await ConfirmAsync(s, Regular())).ShouldBe("ok");

        (await InDbAsync(s, db => db.StepUpConfirmations.SingleAsync())).ValidUntil.ShouldBe(s.Time.GetUtcNow() + TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task Only_the_step_up_token_carries_mfa_until_and_another_api_honours_it()
    {
        await using var s = await StartScenarioAsync(realAuthentication: true);
        using var login = await s.Api.Http.PostAsJsonAsync("/api/auth/login", new LoginRequest("10042", "any"));
        var tokens = (await login.Content.ReadFromJsonAsync<TokenResponse>())!;
        using var refreshResponse = await s.Api.Http.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(tokens.RefreshToken));
        var refreshed = (await refreshResponse.Content.ReadFromJsonAsync<TokenResponse>())!;
        await InDbAsync(s, async db =>
        {
            db.EntraLinks.Add(new EntraLinkRecord { EmployeeId = Erika, UserPrincipalName = ErikaUpn, LinkedBy = Admin, LinkedAt = s.Time.GetUtcNow() });
            return await db.SaveChangesAsync();
        });
        var cache = new InMemoryPermissionCache();
        cache.Grant(Erika, TestGrant.Everywhere("payslip:read"));
        await using var consumer = await StartConsumerAsync(s, cache);

        // Login and refresh: no claim, so the privilege that needs the second factor asks for it.
        var handler = new JsonWebTokenHandler();
        handler.ReadJsonWebToken(tokens.AccessToken).Claims.ShouldNotContain(c => c.Type == "mfa_until");
        handler.ReadJsonWebToken(refreshed.AccessToken).Claims.ShouldNotContain(c => c.Type == "mfa_until");
        var plain = await GetPayslips(consumer, refreshed.AccessToken);
        plain.Status.ShouldBe(HttpStatusCode.Forbidden);
        plain.Title.ShouldBe("SecondFactorRequired");

        // The step-up.
        var start = await Post(s, "/api/auth/step-up/start", refreshed.AccessToken);
        var url = (await start.Content.ReadFromJsonAsync<StepUpStartResponse>())!.AuthorizationUrl;
        var (code, state) = s.Entra.SignIn(url, Regular());
        (await CallbackAsync(s, $"?code={code}&state={state}")).Result.ShouldBe("ok");
        var tokenResponse = await Post(s, "/api/auth/step-up/token", refreshed.AccessToken);
        tokenResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var stepUp = (await tokenResponse.Content.ReadFromJsonAsync<StepUpTokenResponse>())!;

        var jwt = handler.ReadJsonWebToken(stepUp.AccessToken);
        jwt.GetClaim("employee_id").Value.ShouldBe(Erika.ToString());
        long.Parse(jwt.GetClaim("mfa_until").Value, System.Globalization.CultureInfo.InvariantCulture).ShouldBe(s.Time.GetUtcNow().AddMinutes(15).ToUnixTimeSeconds());
        (await GetPayslips(consumer, stepUp.AccessToken)).Status.ShouldBe(HttpStatusCode.OK);

        // Fifteen minutes later the same token no longer opens it, the old plain token never did.
        s.Time.Advance(TimeSpan.FromMinutes(15));
        var late = await GetPayslips(consumer, stepUp.AccessToken);
        late.Status.ShouldBe(HttpStatusCode.Forbidden);
        late.Title.ShouldBe("SecondFactorRequired");
        (await Post(s, "/api/auth/step-up/token", refreshed.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    private static async Task<HttpResponseMessage> Post(Scenario s, string url, string bearer)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return await s.Api.Http.SendAsync(request);
    }

    /// <summary>Another API (as the payroll will be) with a privilege that needs the second factor; it validates Access's tokens locally.</summary>
    private static async Task<WebApplication> StartConsumerAsync(Scenario s, InMemoryPermissionCache cache)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Authority"] = "https://access.local",
            ["Authentication:Audience"] = "newworld",
        });
        builder.Services.AddSingleton<TimeProvider>(s.Time);
        builder.Services.AddSingleton<IPermissionCache>(cache);
        builder.Services.AddSharedJwtAuthentication(builder.Configuration);
        builder.Services.AddSharedAuthorization();
        builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options => options.BackchannelHttpHandler = s.Api.Server.CreateHandler());
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/payslips", () => "ok").RequireAuthorization(AppPolicies.PrivilegeWithSecondFactor("payslip:read"));
        await app.StartAsync();
        return app;
    }

    private static async Task<(HttpStatusCode Status, string? Title)> GetPayslips(WebApplication consumer, string token)
    {
        using var client = consumer.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/payslips");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, body.Length > 0 && body[0] == '{' ? JsonDocument.Parse(body).RootElement.GetProperty("title").GetString() : null);
    }

    // -------------------------------------------------------------- privacy

    [Fact]
    public async Task Logs_contain_failure_codes_but_never_tokens_codes_states_names_or_the_secret()
    {
        await using var s = await StartScenarioAsync();
        await LinkAsync(s);
        var secrets = new List<string> { EntraStandIn.ClientSecret, ErikaUpn, ErikaOid.ToString(), "another-nonce" };

        var okUrl = await StartUrlAsync(s);
        var (okCode, okState) = s.Entra.SignIn(okUrl, Regular());
        (await CallbackAsync(s, $"?code={okCode}&state={okState}")).Result.ShouldBe("ok");
        var failUrl = await StartUrlAsync(s);
        var (failCode, failState) = s.Entra.SignIn(failUrl, Regular() with { Amr = ["pwd"], Nonce = "another-nonce" });
        (await CallbackAsync(s, $"?code={failCode}&state={failState}")).Result.ShouldBe("failed");
        (await CallbackAsync(s, $"?code={failCode}&state={failState}")).Result.ShouldBe("failed");
        secrets.AddRange([okCode, okState, failCode, failState, .. s.Entra.IdTokens]);
        secrets.AddRange(new[] { okUrl, failUrl }.Select(u => QueryHelpers.ParseQuery(new Uri(u).Query)).SelectMany(q => new[] { q["nonce"].ToString(), q["code_challenge"].ToString() }));

        s.Logs.Lines.ShouldNotBeEmpty();
        s.Logs.Lines.ShouldContain(l => l.Contains("Step-up failed:", StringComparison.Ordinal));
        foreach (var secret in secrets)
        {
            s.Logs.Lines.Where(l => l.Contains(secret, StringComparison.Ordinal)).ShouldBeEmpty(secret);
        }
    }

    // --------------------------------------------------------------- options

    [Fact]
    public void Enabled_options_name_missing_settings_and_a_switched_off_step_up_needs_none()
    {
        new StepUpOptions().Problems().ShouldBeEmpty();
        new StepUpOptions { Enabled = true }.Problems().ShouldBe(["TenantId", "ClientId", "ClientSecret", "RedirectUri", "ReturnUrl"]);
        new StepUpOptions { Enabled = true, TenantId = "t", ClientId = "c", ClientSecret = "s", RedirectUri = "not a uri", ReturnUrl = "/step-up" }.Problems().ShouldBe(["RedirectUri"]);
        new StepUpOptions { Enabled = true, TenantId = "t", ClientId = "c", ClientSecret = "s", RedirectUri = "https://a/b", ReturnUrl = "/x", Validity = TimeSpan.Zero }.Problems().ShouldBe(["Validity"]);
        new StepUpOptions { TenantId = "t" }.EffectiveAuthority.ShouldBe("https://login.microsoftonline.com/t/v2.0");
        new StepUpOptions().Validity.ShouldBe(TimeSpan.FromMinutes(15));
        new StepUpOptions().Enabled.ShouldBeFalse();
    }

    [Fact]
    public async Task Starting_with_an_enabled_but_incomplete_step_up_fails_naming_the_setting_not_a_value()
    {
        await using var factory = new IncompleteStepUpFactory(await postgres.CreateDatabaseAsync());

        var ex = Should.Throw<Exception>(() => _ = factory.Services);

        ex.ToString().ShouldContain("Access:StepUp");
        ex.ToString().ShouldContain("ClientSecret");
        ex.ToString().ShouldNotContain("tenant-secret-value");
    }

    private sealed class IncompleteStepUpFactory(string connectionString) : WebApplicationFactory<ApiMarker>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting($"ConnectionStrings:{InfrastructureServiceCollectionExtensions.ConnectionName}", connectionString);
            builder.UseSetting("Access:StepUp:Enabled", "true");
            builder.UseSetting("Access:StepUp:TenantId", "tenant-secret-value");
        }
    }
}
