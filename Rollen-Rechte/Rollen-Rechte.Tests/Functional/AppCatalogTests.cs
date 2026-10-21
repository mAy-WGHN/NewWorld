using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Rollen_Rechte.Domain.Entities;
using Rollen_Rechte.Tests.Infrastructure;

namespace Rollen_Rechte.Tests.Functional;

/// <summary>Andere Anwendungen (Portal, HR-Tool) melden beim Start ihren Rechtekatalog an; dieser Dienst führt die Kataloge zusammen.</summary>
public class AppCatalogTests(ApiFactory factory) : ApiTestBase(factory)
{
    /// <summary>Eindeutiger Bereich je Test (die Tabelle ist von anderen Tests gefüllt): Bereich "kat" + 8 Zeichen.</summary>
    private static string NewArea() => "kat" + Guid.NewGuid().ToString("N")[..8];

    private static object Catalog(string area, params (string Action, bool Scopable)[] actions) => new
    {
        areas = new[]
        {
            new
            {
                key = area, label = "Beispielbereich", definesReach = true,
                actions = actions.Select(a => new { privilege = $"{area}:{a.Action}", label = a.Action, scopable = a.Scopable }).ToArray()
            }
        }
    };

    private Task<HttpResponseMessage> RegisterAsync(string appKey, object body, string? apiKey = ApiFactory.AppApiKey) =>
        SendAsync(HttpMethod.Put, $"/api/apps/{appKey}/catalog", body, configure: request =>
        {
            if (apiKey is not null) request.Headers.Add("X-Api-Key", apiKey);
        });

    private async Task<List<JsonElement>> CatalogAreasAsync(string token) =>
        (await DataAsync(await GetAsync("/api/privileges", token))).EnumerateArray().ToList();

    // --- Zugang ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Register_NeedsTheApplicationsOwnKey()
    {
        var body = Catalog(NewArea(), ("read", true));

        Assert.Equal(HttpStatusCode.Unauthorized, (await RegisterAsync(ApiFactory.AppKey, body, apiKey: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RegisterAsync(ApiFactory.AppKey, body, apiKey: "falsch")).StatusCode);
        // Ein Schlüssel gilt nur für die eigene Anwendung.
        Assert.Equal(HttpStatusCode.Unauthorized, (await RegisterAsync("hr", body)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(ApiFactory.AppKey, body)).StatusCode);
    }

    [Fact]
    public async Task Register_IsNotReachableWithAUserToken()
    {
        var (_, admin) = await AdminWithTokenAsync();

        var response = await SendAsync(HttpMethod.Put, $"/api/apps/{ApiFactory.AppKey}/catalog", Catalog(NewArea(), ("read", true)), admin);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- Zusammenführen ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task RegisteredRights_AppearInTheCatalog_WithTheirScopability()
    {
        var (_, admin) = await AdminWithTokenAsync();
        var area = NewArea();

        var response = await RegisterAsync(ApiFactory.AppKey, Catalog(area, ("read", true), ("delete", false)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, (await DataAsync(response)).GetProperty("privileges").GetInt32());
        var registered = (await CatalogAreasAsync(admin)).Single(a => a.GetProperty("key").GetString() == area);
        Assert.Equal("Beispielbereich", registered.GetProperty("label").GetString());
        var read = registered.GetProperty("actions").EnumerateArray().Single(a => a.GetProperty("privilege").GetString() == $"{area}:read");
        Assert.True(read.GetProperty("scopable").GetBoolean());
        Assert.False(read.GetProperty("custom").GetBoolean());
        var delete = registered.GetProperty("actions").EnumerateArray().Single(a => a.GetProperty("privilege").GetString() == $"{area}:delete");
        Assert.False(delete.GetProperty("scopable").GetBoolean());
    }

    [Fact]
    public async Task Register_ReplacesThePreviousCatalogOfTheSameApplication()
    {
        var (_, admin) = await AdminWithTokenAsync();
        var area = NewArea();
        await RegisterAsync(ApiFactory.AppKey, Catalog(area, ("read", true), ("old", false)));

        await RegisterAsync(ApiFactory.AppKey, Catalog(area, ("read", false), ("new", true)));

        var privileges = (await CatalogAreasAsync(admin)).Single(a => a.GetProperty("key").GetString() == area)
            .GetProperty("actions").EnumerateArray().Select(a => a.GetProperty("privilege").GetString()).Order().ToList();
        Assert.Equal([$"{area}:new", $"{area}:read"], privileges);
        var read = (await CatalogAreasAsync(admin)).Single(a => a.GetProperty("key").GetString() == area)
            .GetProperty("actions").EnumerateArray().Single(a => a.GetProperty("privilege").GetString() == $"{area}:read");
        Assert.False(read.GetProperty("scopable").GetBoolean()); // aktualisiert, nicht dupliziert
        Assert.Equal(2, await QueryDbAsync(db => db.AppPrivileges.CountAsync(p => p.AreaKey == area)));
    }

    [Fact]
    public async Task RegisteredRights_CanBeGivenToRoles_AndAreResolvedForTheUi()
    {
        var (_, admin) = await AdminWithTokenAsync();
        var area = NewArea();
        await RegisterAsync(ApiFactory.AppKey, Catalog(area, ("read", true), ("update", true)));

        Assert.Equal(HttpStatusCode.Created,
            (await PostAsync("/api/roles", new { name = Unique("Rolle"), privileges = new[] { $"{area}:read", $"{area}:*" } }, admin)).StatusCode);
        var (_, token) = await UserWithPrivilegesAsync($"{area}:*");

        var mine = (await DataAsync(await GetAsync("/api/me/privileges", token))).GetProperty("privileges").EnumerateArray().Select(p => p.GetString()).ToList();

        Assert.Equal([$"{area}:read", $"{area}:update"], mine);
    }

    [Fact]
    public async Task ARegisteredScopableRight_CanBeGivenScoped_AndAnUnregisteredOneIsInert()
    {
        var (_, admin) = await AdminWithTokenAsync();
        var area = NewArea();
        await RegisterAsync(ApiFactory.AppKey, Catalog(area, ("read", true), ("delete", false)));
        var location = await CreateLocationAsync();
        var scope = await Factory.WithDbAsync(async (db, _) =>
        {
            var s = new Scope { Name = Unique("Scope"), Members = [new ScopeMember { Dimension = ScopeDimensions.Location, RefId = location.Id }] };
            db.Scopes.Add(s);
            await db.SaveChangesAsync();
            return s.Id;
        });
        var user = await CreateAdminAsync(withAdminRole: false);
        var role = await Factory.WithDbAsync(async (db, _) =>
        {
            var r = new AclRole { Name = Unique("Rolle"), Privileges = [$"{area}:read", $"{area}:delete"] };
            db.AclRoles.Add(r);
            await db.SaveChangesAsync();
            return r.Id;
        });

        var assign = await SendAsync(HttpMethod.Put, $"/api/users/{user.Id}/roles",
            new { assignments = new[] { new { roleId = role, scoped = true, scopeIds = new[] { scope } } } }, admin);
        Assert.Equal(HttpStatusCode.OK, assign.StatusCode);

        var token = await LoginAsync(user.Username);
        var mine = await DataAsync(await GetAsync("/api/me/privileges", token));
        var scopes = mine.GetProperty("scopes");
        Assert.False(scopes.GetProperty($"{area}:read").GetProperty("all").GetBoolean());
        Assert.Equal([location.Id], scopes.GetProperty($"{area}:read").GetProperty("locationIds").EnumerateArray().Select(l => l.GetGuid()));
        // Nicht scopbar: Die eingeschränkte Zuweisung wirkt dafür nicht.
        Assert.False(scopes.TryGetProperty($"{area}:delete", out _));
    }

    // --- Prüfungen ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Register_RejectsRightsOfThisServiceAndOfOtherApplications()
    {
        var area = NewArea();
        Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(ApiFactory.AppKey, Catalog(area, ("read", true)))).StatusCode);
        // Ein zweiter App-Schlüssel, der nur in diesem Test existiert.
        await Factory.WithDbAsync(async (db, _) =>
        {
            db.AppPrivileges.Add(new AppPrivilege { AppKey = "hr", Privilege = $"{area}:fremd", Label = "x", AreaKey = area, AreaLabel = "x" });
            await db.SaveChangesAsync();
        });

        var own = await RegisterAsync(ApiFactory.AppKey, Catalog("user", ("read", true)));
        var foreign = await RegisterAsync(ApiFactory.AppKey, Catalog(area, ("fremd", true)));

        Assert.Equal(HttpStatusCode.Conflict, own.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, foreign.StatusCode);
    }

    [Fact]
    public async Task Register_RejectsRightsThatExistAsCustomRights()
    {
        var (_, admin) = await AdminWithTokenAsync();
        var area = NewArea();
        Assert.Equal(HttpStatusCode.Created, (await PostAsync("/api/custom-privileges", new { key = $"{area}:eigen", label = "Eigen" }, admin)).StatusCode);

        var response = await RegisterAsync(ApiFactory.AppKey, Catalog(area, ("eigen", true)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Register_ValidatesTheCatalog()
    {
        var area = NewArea();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await RegisterAsync(ApiFactory.AppKey, new { })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await RegisterAsync(ApiFactory.AppKey, new
        {
            areas = new[] { new { key = area, label = "x", actions = new[] { new { privilege = "falsch:aktion", label = "x" } } } }
        })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await RegisterAsync(ApiFactory.AppKey, new
        {
            areas = new[] { new { key = "Gross", label = "x", actions = new[] { new { privilege = "Gross:aktion", label = "x" } } } }
        })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await RegisterAsync(ApiFactory.AppKey, new
        {
            areas = new[] { new { key = area, label = "x", actions = new[] { new { privilege = $"{area}:a", label = "x" }, new { privilege = $"{area}:a", label = "y" } } } }
        })).StatusCode);
    }

    [Fact]
    public async Task ARemovedRegistration_LeavesRolesInert_NotBroken()
    {
        var (_, admin) = await AdminWithTokenAsync();
        var area = NewArea();
        await RegisterAsync(ApiFactory.AppKey, Catalog(area, ("read", true)));
        var (_, token) = await UserWithPrivilegesAsync($"{area}:read");
        var before = (await DataAsync(await GetAsync("/api/me/privileges", token))).GetProperty("privileges").GetArrayLength();
        Assert.Equal(1, before);

        await RegisterAsync(ApiFactory.AppKey, new { areas = Array.Empty<object>() });

        var after = (await DataAsync(await GetAsync("/api/me/privileges", token))).GetProperty("privileges").GetArrayLength();
        Assert.Equal(0, after);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await PostAsync("/api/roles", new { name = Unique("Rolle"), privileges = new[] { $"{area}:read" } }, admin)).StatusCode);
    }

    // --- Filialen für Anwendungen ------------------------------------------------------------------------------------

    [Fact]
    public async Task Locations_ListsActiveAndArchivedLocations_ForTheApplication()
    {
        var active = await CreateLocationAsync();
        var archived = await CreateLocationAsync(archived: true);

        var response = await SendAsync(HttpMethod.Get, $"/api/apps/{ApiFactory.AppKey}/locations",
            configure: r => r.Headers.Add("X-Api-Key", ApiFactory.AppApiKey));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rows = (await DataAsync(response)).EnumerateArray().ToList();
        Assert.Equal(active.Name, rows.Single(l => l.GetProperty("id").GetGuid() == active.Id).GetProperty("name").GetString());
        Assert.True(rows.Single(l => l.GetProperty("id").GetGuid() == archived.Id).GetProperty("archived").GetBoolean());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key")]
    public async Task Locations_RequireTheApplicationKey(string? apiKey)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/apps/{ApiFactory.AppKey}/locations",
            configure: r => { if (apiKey is not null) r.Headers.Add("X-Api-Key", apiKey); });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
