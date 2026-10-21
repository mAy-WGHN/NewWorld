using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using Rollen_Rechte.Authentication;
using Rollen_Rechte.Tests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Rollen_Rechte.Tests.Functional;

public class PrivilegeTests(ApiFactory factory) : ApiTestBase(factory)
{
    private static readonly string SomeId = Guid.NewGuid().ToString();

    private sealed record ProtectedRoute(string Method, string Url, string Privilege)
    {
        public override string ToString() => $"{Method} {Url} ({Privilege})";
    }

    /// <summary>
    /// Alle Controller-Routen mit [HasPrivilege], direkt aus den Endpunkt-Metadaten gelesen: Es gibt keine zweite Liste zu pflegen.
    /// Platzhalter im Pfad werden durch eine zufällige Guid ersetzt (die Rechteprüfung läuft vor jeder Suche und Validierung).
    /// </summary>
    private IReadOnlyList<ProtectedRoute> ProtectedRoutes() =>
        Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<ControllerActionDescriptor>() is not null)
            .Select(e => (Endpoint: e, Policy: e.Metadata.GetOrderedMetadata<IAuthorizeData>()
                .Select(a => a.Policy).LastOrDefault(p => p?.StartsWith(AuthPolicies.PrivilegePrefix) ?? false)))
            .Where(x => x.Policy is not null)
            .Select(x => new ProtectedRoute(
                x.Endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods.First(),
                "/" + Regex.Replace(x.Endpoint.RoutePattern.RawText!.TrimStart('/'), @"\{[^}]+\}", SomeId),
                x.Policy![AuthPolicies.PrivilegePrefix.Length..]))
            .OrderBy(r => r.Url).ThenBy(r => r.Method)
            .ToList();

    private Task<HttpResponseMessage> CallAsync(ProtectedRoute route, string? token)
    {
        UseNewIp(); // jede Anfrage mit eigenem Rate-Limit-Budget
        var hasBody = route.Method is "POST" or "PUT" or "PATCH";
        return SendAsync(new HttpMethod(route.Method), route.Url, hasBody ? new { } : null, token);
    }
    private async Task<string> TokenAsync(params string[] privileges)
    {
        UseNewIp();
        return (await UserWithPrivilegesAsync(privileges)).Token;
    }

    private static string Describe(HttpResponseMessage response, ProtectedRoute route, HttpStatusCode expected) =>
        $"{route}: erwartet {expected}, erhalten {response.StatusCode}";

    [Fact]
    public async Task ProtectedRoutes_AreDiscoveredFromTheEndpoints()
    {
        var routes = ProtectedRoutes();

        Assert.True(routes.Count >= 25, $"Nur {routes.Count} geschützte Routen gefunden.");
        Assert.Contains(routes, r => r is { Method: "PUT", Privilege: "role:assign" });
        await Task.CompletedTask;
    }

    [Fact]
    public async Task EveryProtectedRoute_WithoutToken_Returns401()
    {
        var failures = new List<string>();
        foreach (var route in ProtectedRoutes())
        {
            var response = await CallAsync(route, null);
            if (response.StatusCode != HttpStatusCode.Unauthorized) failures.Add(Describe(response, route, HttpStatusCode.Unauthorized));
        }

        Assert.True(failures.Count == 0, string.Join(" | ", failures));
    }

    [Fact]
    public async Task EveryProtectedRoute_WithoutAnyRole_Returns403WithEnvelope()
    {
        var token = await TokenAsync();
        var failures = new List<string>();
        foreach (var route in ProtectedRoutes())
        {
            var response = await CallAsync(route, token);
            if (response.StatusCode != HttpStatusCode.Forbidden)
            {
                failures.Add(Describe(response, route, HttpStatusCode.Forbidden));
                continue;
            }

            var body = await JsonAsync(response);
            var expected = AuthMessages.ForAction(route.Privilege[(route.Privilege.IndexOf(':') + 1)..]);
            if (body.GetProperty("success").GetBoolean() || body.GetProperty("message").GetString() != expected)
                failures.Add($"{route}: 403 ohne die vordefinierte Meldung \"{expected}\"");
        }

        Assert.True(failures.Count == 0, string.Join(" | ", failures));
    }

    [Fact]
    public async Task EveryProtectedRoute_WithAnotherPrivilege_Returns403()
    {
        var scopeToken = await TokenAsync("scope:read");
        var userToken = await TokenAsync("user:read");
        var failures = new List<string>();
        foreach (var route in ProtectedRoutes())
        {
            var response = await CallAsync(route, route.Privilege == "user:read" ? scopeToken : userToken);
            if (response.StatusCode != HttpStatusCode.Forbidden) failures.Add(Describe(response, route, HttpStatusCode.Forbidden));
        }

        Assert.True(failures.Count == 0, string.Join(" | ", failures));
    }

    [Fact]
    public async Task EveryProtectedRoute_WithItsExactPrivilege_PassesTheCheck()
    {
        var routes = ProtectedRoutes();
        var tokens = new Dictionary<string, string>();
        foreach (var privilege in routes.Select(r => r.Privilege).Distinct())
            tokens[privilege] = await TokenAsync(privilege);

        var failures = new List<string>();
        foreach (var route in routes)
        {
            var response = await CallAsync(route, tokens[route.Privilege]);
            // Weiter als bis zur Rechteprüfung: 404/422/400 o. Ä. je nach Route, aber nie 401/403.
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                failures.Add($"{route}: {response.StatusCode}");
        }

        Assert.True(failures.Count == 0, string.Join(" | ", failures));
    }

    [Fact]
    public async Task EveryRoute_DeclaresItsAccess()
    {
        // Jede Controller-Route ist ausdrücklich öffentlich, "nur angemeldet" oder an ein Recht gebunden.
        var endpoints = Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .Where(e => e.Metadata.GetMetadata<ControllerActionDescriptor>() is not null)
            .ToList();
        Assert.NotEmpty(endpoints);

        var undeclared = endpoints.Where(e =>
        {
            if (e.Metadata.GetMetadata<IAllowAnonymous>() is not null) return false;
            var policies = e.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).ToList();
            return !policies.Any(p => p == AuthPolicies.Authenticated || (p?.StartsWith(AuthPolicies.PrivilegePrefix) ?? false) || (p?.StartsWith(AuthPolicies.CustomPrefix) ?? false));
        }).Select(e => e.DisplayName).ToList();

        Assert.Empty(undeclared);
        await Task.CompletedTask;
    }

    // --- Katalog <-> Routen ---------------------------------------------------------------------------------

    [Fact]
    public async Task Catalog_MatchesTheRoutes_EveryPrivilegeIsRequiredAndEveryRequirementIsInTheCatalog()
    {
        var required = ProtectedRoutes().Select(r => r.Privilege).ToHashSet();

        Assert.Empty(required.Except(PrivilegeCatalog.All).Order());          // Route verlangt ein Recht, das nicht im Katalog steht
        Assert.Empty(PrivilegeCatalog.All.Except(required).Order());          // Katalogeintrag, den keine Route verlangt
        await Task.CompletedTask;
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
    // --- Verhalten ------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("*")]
    [InlineData("user:*")]
    public async Task Wildcards_GrantTheMatchingRoutes(string privilege)
    {
        var (_, token) = await UserWithPrivilegesAsync(privilege);

        Assert.Equal(HttpStatusCode.OK, (await GetAsync("/api/users", token)).StatusCode);
        var locations = await GetAsync("/api/locations/archived", token);
        Assert.Equal(privilege == "*" ? HttpStatusCode.OK : HttpStatusCode.Forbidden, locations.StatusCode);
    }

    [Fact]
    public async Task User_CombinesThePrivilegesOfAllHisRoles()
    {
        var (user, token) = await UserWithPrivilegesAsync("user:read");
        await Factory.WithDbAsync(async (db, _) =>
        {
            var role = new Rollen_Rechte.Domain.Entities.AclRole { Name = Unique("Rolle"), Privileges = ["location:read"] };
            db.AclRoles.Add(role);
            db.AclUserRoles.Add(new Rollen_Rechte.Domain.Entities.AclUserRole { UserId = user.Id, AclRoleId = role.Id });
            await db.SaveChangesAsync();
        });

        Assert.Equal(HttpStatusCode.OK, (await GetAsync("/api/users", token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync("/api/locations/archived", token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync("/api/scopes", token)).StatusCode);
    }

    [Fact]
    public async Task RemovedRole_TakesEffectImmediately_EvenWithAValidToken()
    {
        var (user, token) = await UserWithPrivilegesAsync("user:read");
        Assert.Equal(HttpStatusCode.OK, (await GetAsync("/api/users", token)).StatusCode);

        await Factory.WithDbAsync(async (db, _) =>
            await db.AclUserRoles.Where(x => x.UserId == user.Id).ExecuteDeleteAsync());

        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync("/api/users", token)).StatusCode);
    }

    [Fact]
    public async Task OwnDataRoutes_NeedOnlyTheLogin()
    {
        var (_, token) = await UserWithPrivilegesAsync();

        Assert.Equal(HttpStatusCode.OK, (await GetAsync("/api/me/privileges", token)).StatusCode);
        var password = await PatchAsync("/api/me/password", new { currentPassword = "falsch", newPassword = "egal-egal-123" }, token);
        Assert.NotEqual(HttpStatusCode.Forbidden, password.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, password.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync("/api/me/privileges")).StatusCode);
    }

    [Fact]
    public async Task PublicRoutes_StayPublic()
    {
        Assert.Equal(HttpStatusCode.OK, (await GetAsync("/api/ping")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync("/api/locations")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync("/health")).StatusCode);
    }

}
