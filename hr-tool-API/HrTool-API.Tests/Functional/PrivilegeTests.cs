using System.Net;
using System.Text.Json;
using HrTool_API.Authentication;
using HrTool_API.Tests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HrTool_API.Tests.Functional;

/// <summary>
/// Jede Route verlangt ihr Recht von Rollen-Rechte (hier ersetzt durch <see cref="FakeAccessDirectory"/>); Katalog und Routen entsprechen einander genau.
/// Das Verhalten der Rechteprüfung zeigt die Test-Route <c>/api/probe</c> (<see cref="ProbeController"/>), solange das HR-Tool keine eigenen geschützten Routen hat.
/// </summary>
public class PrivilegeTests(ApiFactory factory) : ApiTestBase(factory)
{
    private const string Probe = "/api/probe";

    /// <summary>Die Controller-Routen der Anwendung selbst (ohne die Test-Routen).</summary>
    private IEnumerable<RouteEndpoint> AppEndpoints() =>
        Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<ControllerActionDescriptor>() is { } d &&
                        d.ControllerTypeInfo.Assembly == typeof(Program).Assembly);

    private static IEnumerable<string> RequiredPrivileges(IEnumerable<RouteEndpoint> endpoints) =>
        endpoints.Select(e => e.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy)
                .LastOrDefault(p => p?.StartsWith(AuthPolicies.PrivilegePrefix) ?? false))
            .Where(p => p is not null)
            .Select(p => p![AuthPolicies.PrivilegePrefix.Length..]);

    // --- Katalog <-> Routen ---------------------------------------------------------------------------------

    [Fact]
    public void EveryRoute_DeclaresItsAccess()
    {
        // Jede Controller-Route ist ausdrücklich öffentlich oder an ein Recht gebunden.
        var endpoints = AppEndpoints().ToList();
        Assert.NotEmpty(endpoints);

        var undeclared = endpoints.Where(e =>
        {
            if (e.Metadata.GetMetadata<IAllowAnonymous>() is not null) return false;
            var policies = e.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).ToList();
            return !policies.Any(p => p?.StartsWith(AuthPolicies.PrivilegePrefix) ?? false);
        }).Select(e => e.DisplayName).ToList();

        Assert.Empty(undeclared);
    }

    [Fact]
    public void Catalog_MatchesTheRoutes_EveryPrivilegeIsRequiredAndEveryRequirementIsInTheCatalog()
    {
        var required = RequiredPrivileges(AppEndpoints()).ToHashSet();

        Assert.Empty(required.Except(PrivilegeCatalog.All).Order());          // Route verlangt ein Recht, das nicht im Katalog steht
        Assert.Empty(PrivilegeCatalog.All.Except(required).Order());          // Katalogeintrag, den keine Route verlangt
    }

    [Fact]
    public void Catalog_HasConsistentAreasAndKeys()
    {
        Assert.All(PrivilegeCatalog.Areas, area =>
        {
            Assert.NotEmpty(area.Actions);
            Assert.All(area.Actions, a => Assert.StartsWith(area.Key + ":", a.Privilege));
        });
        Assert.Equal(PrivilegeCatalog.Areas.Count, PrivilegeCatalog.Areas.Select(a => a.Key).Distinct().Count());
    }

    // --- Jede geschützte Route der Anwendung ---------------------------------------------------------------------

    private sealed record ProtectedRoute(string Method, string Url, string Privilege)
    {
        public override string ToString() => $"{Method} {Url} ({Privilege})";
    }

    /// <summary>
    /// Alle Routen der Anwendung mit [HasPrivilege], direkt aus den Endpunkt-Metadaten gelesen: Es gibt keine zweite Liste zu pflegen.
    /// Platzhalter im Pfad werden durch eine zufällige Guid ersetzt (die Rechteprüfung läuft vor jeder Suche und Validierung).
    /// </summary>
    private IReadOnlyList<ProtectedRoute> ProtectedRoutes()
    {
        var someId = Guid.NewGuid().ToString();
        return AppEndpoints()
            .Select(e => (Endpoint: e, Policy: e.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy)
                .LastOrDefault(p => p?.StartsWith(AuthPolicies.PrivilegePrefix) ?? false)))
            .Where(x => x.Policy is not null)
            .Select(x => new ProtectedRoute(
                x.Endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()!.HttpMethods.First(),
                "/" + System.Text.RegularExpressions.Regex.Replace(x.Endpoint.RoutePattern.RawText!.TrimStart('/'), @"\{[^}]+\}", someId),
                x.Policy![AuthPolicies.PrivilegePrefix.Length..]))
            .OrderBy(r => r.Url).ThenBy(r => r.Method)
            .ToList();
    }

    private Task<HttpResponseMessage> CallAsync(ProtectedRoute route, string? token)
    {
        UseNewIp(); // jede Anfrage mit eigenem Rate-Limit-Budget
        return SendAsync(new HttpMethod(route.Method), route.Url, route.Method is "POST" or "PUT" or "PATCH" ? new { } : null, token);
    }

    private async Task<string> TokenAsync(params string[] privileges)
    {
        UseNewIp();
        return (await UserWithPrivilegesAsync(privileges)).Token;
    }

    [Fact]
    public void ProtectedRoutes_AreDiscoveredFromTheEndpoints()
    {
        var routes = ProtectedRoutes();

        Assert.True(routes.Count >= 8, $"Nur {routes.Count} geschützte Routen gefunden.");
        Assert.Contains(routes, r => r is { Method: "GET", Privilege: "schedule:read" });
        Assert.DoesNotContain(routes, r => r.Privilege.StartsWith("probe:"));
    }

    [Fact]
    public async Task EveryProtectedRoute_WithoutToken_Returns401()
    {
        var failures = new List<string>();
        foreach (var route in ProtectedRoutes())
            if ((await CallAsync(route, null)).StatusCode != HttpStatusCode.Unauthorized) failures.Add(route.ToString());

        Assert.True(failures.Count == 0, string.Join(" | ", failures));
    }

    [Fact]
    public async Task EveryProtectedRoute_WithoutAnyRole_Returns403WithThePredefinedMessage()
    {
        var token = await TokenAsync();
        var failures = new List<string>();
        foreach (var route in ProtectedRoutes())
        {
            var response = await CallAsync(route, token);
            var expected = AuthMessages.ForAction(route.Privilege[(route.Privilege.IndexOf(':') + 1)..]);
            if (response.StatusCode != HttpStatusCode.Forbidden || (await JsonAsync(response)).GetProperty("message").GetString() != expected)
                failures.Add($"{route}: {response.StatusCode}");
        }

        Assert.True(failures.Count == 0, string.Join(" | ", failures));
    }

    [Fact]
    public async Task EveryProtectedRoute_WithEveryOtherPrivilege_Returns403()
    {
        var routes = ProtectedRoutes();
        var failures = new List<string>();
        foreach (var route in routes)
        {
            // Alle Rechte des Katalogs außer dem, das die Route verlangt.
            var token = await TokenAsync([.. PrivilegeCatalog.All.Where(p => p != route.Privilege)]);
            if ((await CallAsync(route, token)).StatusCode != HttpStatusCode.Forbidden) failures.Add(route.ToString());
        }

        Assert.True(failures.Count == 0, string.Join(" | ", failures));
    }

    [Fact]
    public async Task EveryProtectedRoute_WithItsExactPrivilege_PassesTheCheck()
    {
        var routes = ProtectedRoutes();
        var failures = new List<string>();
        foreach (var route in routes)
        {
            var response = await CallAsync(route, await TokenAsync(route.Privilege));
            // Weiter als bis zur Rechteprüfung: 404/422 o. Ä. je nach Route, aber nie 401/403.
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) failures.Add($"{route}: {response.StatusCode}");
        }

        Assert.True(failures.Count == 0, string.Join(" | ", failures));
    }

    // --- Verhalten ------------------------------------------------------------------------------------------

    [Fact]
    public async Task WithoutAToken_Is401WithTheEnvelope()
    {
        var response = await GetAsync(Probe);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Equal("JWT Token not found", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task WithoutTheRequiredPrivilege_Is403WithThePredefinedMessage()
    {
        var (_, token) = await UserWithPrivilegesAsync("other:read");

        var response = await GetAsync(Probe, token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Equal(AuthMessages.Read, body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task WithoutAnyRole_Is403()
    {
        var (_, token) = await UserWithPrivilegesAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(Probe, token)).StatusCode);
    }

    [Fact]
    public async Task WithThePrivilege_PassesTheCheck_AndTheScopeIsAvailableToTheQueries()
    {
        var location = CreateLocation();
        var (_, everything) = await UserWithPrivilegesAsync(ProbeController.Privilege);
        var (_, scoped) = await ScopedUserAsync([ProbeController.Privilege], [location.Id]);

        var all = await OkBodyAsync(await GetAsync(Probe, everything));
        var some = await OkBodyAsync(await GetAsync(Probe, scoped));

        Assert.True(all.GetProperty("all").GetBoolean());
        Assert.False(some.GetProperty("all").GetBoolean());
        Assert.Equal([location.Id], some.GetProperty("locationIds").EnumerateArray().Select(l => l.GetGuid()));
    }

    private static async Task<JsonElement> OkBodyAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await JsonAsync(response);
    }

    [Fact]
    public async Task ATokenRollenRechteDoesNotKnow_IsUnauthorized_EvenWithAValidSignature()
    {
        var (_, token) = await UserWithPrivilegesAsync(ProbeController.Privilege);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(Probe, token)).StatusCode);

        Factory.Access.Revoke(token); // z. B. User deaktiviert

        var response = await GetAsync(Probe, token);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False((await JsonAsync(response)).GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task WhenRollenRechteIsDown_ProtectedRoutesAnswer503_AndPublicOnesStayUp()
    {
        var (_, token) = await UserWithPrivilegesAsync(ProbeController.Privilege);
        Factory.Access.Unavailable = true;

        var response = await GetAsync(Probe, token);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.False((await JsonAsync(response)).GetProperty("success").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await GetAsync("/api/ping")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(Probe + "/open")).StatusCode);
    }

    [Fact]
    public async Task APrivilegeWithoutAScope_IsNotGranted_FailClosed()
    {
        var token = NewToken();
        Factory.Access.SetPrivilegesOnly(token, [ProbeController.Privilege]); // Rollen-Rechte hat keine Reichweite zum Recht geliefert

        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(Probe, token)).StatusCode);
    }

    [Fact]
    public async Task AnInvalidSignature_IsRejectedBeforeAskingRollenRechte()
    {
        var calls = Factory.Access.Calls;

        var response = await GetAsync(Probe, NewToken() + "x");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(calls, Factory.Access.Calls);
    }

    [Fact]
    public async Task AnExpiredToken_Is401()
    {
        var expired = NewToken(expires: DateTime.UtcNow.AddHours(-1));
        Factory.Access.Set(expired, [ProbeController.Privilege], ScopeSet.Everything);

        var response = await GetAsync(Probe, expired);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Expired JWT Token", (await JsonAsync(response)).GetProperty("message").GetString());
    }
}
