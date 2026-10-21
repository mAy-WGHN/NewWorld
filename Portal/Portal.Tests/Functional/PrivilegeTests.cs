using System.Net;
using System.Text.RegularExpressions;
using Portal.Authentication;
using Portal.Tests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Portal.Tests.Functional;

/// <summary>Jede Route verlangt ihr Recht von Rollen-Rechte (hier ersetzt durch <see cref="FakeAccessDirectory"/>); Katalog und Routen entsprechen einander genau.</summary>
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
        // Der Bild-Upload erwartet multipart; mit JSON antwortet MVC schon vor der Rechteprüfung mit 415.
        var multipart = route.Method == "POST" && route.Url.EndsWith("/faq/images");
        return SendAsync(new HttpMethod(route.Method), route.Url, hasBody && !multipart ? new { } : null, token, request =>
        {
            if (!multipart) return;
            request.Content = new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]), "file", "x.bin" } };
        });
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

        Assert.True(routes.Count >= 12, $"Nur {routes.Count} geschützte Routen gefunden.");
        Assert.Contains(routes, r => r is { Method: "GET", Privilege: "report:read" });
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
        var statistic = await TokenAsync(Privileges.StatisticRead);
        var report = await TokenAsync("report:read");
        var failures = new List<string>();
        foreach (var route in ProtectedRoutes())
        {
            var response = await CallAsync(route, route.Privilege == Privileges.StatisticRead ? report : statistic);
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
        // Jede Controller-Route ist ausdrücklich öffentlich oder an ein Recht gebunden.
        var endpoints = Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .Where(e => e.Metadata.GetMetadata<ControllerActionDescriptor>() is not null)
            .ToList();
        Assert.NotEmpty(endpoints);

        var undeclared = endpoints.Where(e =>
        {
            if (e.Metadata.GetMetadata<IAllowAnonymous>() is not null) return false;
            var policies = e.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).ToList();
            return !policies.Any(p => p?.StartsWith(AuthPolicies.PrivilegePrefix) ?? false);
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

    [Fact]
    public async Task ATokenRollenRechteDoesNotKnow_IsUnauthorized_EvenWithAValidSignature()
    {
        var (_, token) = await UserWithPrivilegesAsync("report:read");
        Assert.Equal(HttpStatusCode.OK, (await GetAsync("/api/reports", token)).StatusCode);

        Factory.Access.Revoke(token); // z. B. User deaktiviert

        var response = await GetAsync("/api/reports", token);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False((await JsonAsync(response)).GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task WhenRollenRechteIsDown_ProtectedRoutesAnswer503_AndPublicOnesStayUp()
    {
        var (_, token) = await UserWithPrivilegesAsync("report:read");
        var location = await CreateLocationAsync();
        Factory.Access.Unavailable = true;

        var response = await GetAsync("/api/reports", token);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.False((await JsonAsync(response)).GetProperty("success").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await GetAsync("/api/faqs")).StatusCode);
        var created = await PostAsync("/api/reports",
            new { personnelNumber = 1, category = "bug", severity = "low", description = "x", locationIds = new[] { location.Id } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    }

    [Fact]
    public async Task APrivilegeWithoutAScope_IsNotGranted_FailClosed()
    {
        var token = NewToken();
        Factory.Access.SetPrivilegesOnly(token, ["report:read"]); // Rollen-Rechte hat keine Reichweite zum Recht geliefert

        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync("/api/reports", token)).StatusCode);
    }

    [Fact]
    public async Task AnInvalidSignature_IsRejectedBeforeAskingRollenRechte()
    {
        var calls = Factory.Access.Calls;

        var response = await GetAsync("/api/reports", NewToken() + "x");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(calls, Factory.Access.Calls);
    }

    [Fact]
    public async Task PublicRoutes_StayPublic()
    {
        Assert.Equal(HttpStatusCode.OK, (await GetAsync("/api/ping")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync("/api/faqs")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync("/health")).StatusCode);
    }
}
