using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Rollen_Rechte.Authentication;
using Rollen_Rechte.Domain.Entities;
using Rollen_Rechte.Tests.Infrastructure;

namespace Rollen_Rechte.Tests.Functional;

/// <summary>
/// Scopes: Rollen sagen, was jemand darf, der Scope, wo — samt Gültigkeit (Vertretung/Zweitmeinung) und den vordefinierten 403-Meldungen.
/// Als Probe für "was sieht jemand" dienen die archivierten Filialen (<c>location:read</c>) und die User-Liste (<c>user:read</c>); die Meldungen
/// selbst gehören dem Portal und werden dort geprüft.
/// </summary>
public class ScopeTests(ApiFactory factory) : ApiTestBase(factory)
{
    private static readonly DateTime Past = DateTime.UtcNow.AddDays(-2);
    private static readonly DateTime Future = DateTime.UtcNow.AddDays(2);

    // --- Hilfen ----------------------------------------------------------------------------------------------

    private Task<Guid> ScopeOfAsync(params Guid[] locationIds) =>
        Factory.WithDbAsync(async (db, _) =>
        {
            var scope = new Scope { Name = Unique("Scope"), Members = [.. locationIds.Select(id => new ScopeMember { Dimension = ScopeDimensions.Location, RefId = id })] };
            db.Scopes.Add(scope);
            await db.SaveChangesAsync();
            return scope.Id;
        });

    /// <summary>User mit genau einer eingeschränkten Zuweisung (Rolle mit den Rechten, Scopes, optional Heimatfiliale/Gültigkeit) samt Token.</summary>
    private async Task<(User User, string Token)> ScopedUserAsync(string[] privileges, Guid[]? scopeIds = null, bool homeScope = false,
        Guid? home = null, DateTime? validFrom = null, DateTime? validTo = null)
    {
        var user = await CreateAdminAsync(withAdminRole: false, configure: u => u.HomeLocationId = home);
        await Factory.WithDbAsync(async (db, _) =>
        {
            var role = new AclRole { Name = Unique("Rolle"), Privileges = [.. privileges] };
            db.AclRoles.Add(role);
            db.AclUserRoles.Add(new AclUserRole
            {
                UserId = user.Id, AclRoleId = role.Id, Scoped = true, HomeScope = homeScope, ValidFrom = validFrom, ValidTo = validTo,
                Scopes = [.. (scopeIds ?? []).Select(sid => new AclUserRoleScope { UserId = user.Id, AclRoleId = role.Id, ScopeId = sid })]
            });
            await db.SaveChangesAsync();
        });
        return (user, await LoginAsync(user.Username));
    }

    private Task<Guid> RoleAsync(params string[] privileges) =>
        Factory.WithDbAsync(async (db, _) =>
        {
            var role = new AclRole { Name = Unique("Rolle"), Privileges = [.. privileges] };
            db.AclRoles.Add(role);
            await db.SaveChangesAsync();
            return role.Id;
        });

    /// <summary>Die archivierten Filialen, die der User über <c>location:read</c> sieht.</summary>
    private async Task<List<Guid>> VisibleLocationIdsAsync(string token)
    {
        var response = await GetAsync("/api/locations/archived?limit=500", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await DataAsync(response)).GetProperty("items").EnumerateArray().Select(l => l.GetProperty("id").GetGuid()).ToList();
    }

    private static async Task<string?> MessageAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("message").GetString();

    // --- Sichtbarkeit ----------------------------------------------------------------------------------------

    [Fact]
    public async Task Locations_AScopedUserSeesOnlyTheLocationsOfHisScope()
    {
        var (a, b, c) = (await CreateLocationAsync(archived: true), await CreateLocationAsync(archived: true), await CreateLocationAsync(archived: true));
        var (_, token) = await ScopedUserAsync(["location:read"], [await ScopeOfAsync(a.Id)]);
        var (_, admin) = await AdminWithTokenAsync();

        var visible = await VisibleLocationIdsAsync(token);

        Assert.Equal([a.Id], visible);
        var all = await VisibleLocationIdsAsync(admin);
        Assert.Contains(b.Id, all);
        Assert.Contains(c.Id, all);
    }

    [Fact]
    public async Task Locations_ARegionScopeWithSeveralLocations_CoversAllOfThem()
    {
        var inside = new[] { await CreateLocationAsync(archived: true), await CreateLocationAsync(archived: true), await CreateLocationAsync(archived: true) };
        var outside = await CreateLocationAsync(archived: true);
        var (_, token) = await ScopedUserAsync(["location:read"], [await ScopeOfAsync(inside.Select(l => l.Id).ToArray())]);

        var visible = await VisibleLocationIdsAsync(token);

        Assert.Equal(inside.Select(l => l.Id).Order(), visible.Order());
        Assert.DoesNotContain(outside.Id, visible);
    }

    [Fact]
    public async Task Locations_ScopesOfSeveralAssignmentsAddUp()
    {
        var (a, b, c) = (await CreateLocationAsync(archived: true), await CreateLocationAsync(archived: true), await CreateLocationAsync(archived: true));
        var (user, _) = await ScopedUserAsync(["location:read"], [await ScopeOfAsync(a.Id)]);
        var scopeB = await ScopeOfAsync(b.Id);
        await Factory.WithDbAsync(async (db, _) =>
        {
            var second = new AclRole { Name = Unique("Rolle"), Privileges = ["location:read"] };
            db.AclRoles.Add(second);
            db.AclUserRoles.Add(new AclUserRole
            {
                UserId = user.Id, AclRoleId = second.Id, Scoped = true,
                Scopes = [new AclUserRoleScope { UserId = user.Id, AclRoleId = second.Id, ScopeId = scopeB }]
            });
            await db.SaveChangesAsync();
        });

        var visible = await VisibleLocationIdsAsync(await LoginAsync(user.Username));

        Assert.Equal(new[] { a.Id, b.Id }.Order(), visible.Order());
        Assert.DoesNotContain(c.Id, visible);
    }

    [Fact]
    public async Task AnEmptyScope_SeesNothing_FailClosed()
    {
        var a = await CreateLocationAsync(archived: true);
        var (_, token) = await ScopedUserAsync(["location:read", "location:update"], []);

        Assert.Empty(await VisibleLocationIdsAsync(token));
        var update = await PatchAsync($"/api/locations/{a.Id}", new { name = "nein" }, token);
        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);
        Assert.Equal(AuthMessages.Update, await MessageAsync(update));
    }

    // --- Heimatfiliale ------------------------------------------------------------------------------------------

    [Fact]
    public async Task HomeScope_UsesTheHomeLocationOfTheUser_AndNothingWithoutOne()
    {
        var (a, b) = (await CreateLocationAsync(archived: true), await CreateLocationAsync(archived: true));
        var (_, withHome) = await ScopedUserAsync(["location:read"], homeScope: true, home: a.Id);
        var (_, withoutHome) = await ScopedUserAsync(["location:read"], homeScope: true);

        Assert.Equal([a.Id], await VisibleLocationIdsAsync(withHome));
        Assert.Empty(await VisibleLocationIdsAsync(withoutHome));
        Assert.DoesNotContain(b.Id, await VisibleLocationIdsAsync(withHome));
    }

    [Fact]
    public async Task HomeScope_CombinesWithScopes_ForARegionalManager()
    {
        var locations = new[] { await CreateLocationAsync(archived: true), await CreateLocationAsync(archived: true), await CreateLocationAsync(archived: true) };
        var (_, token) = await ScopedUserAsync(["location:read"], [await ScopeOfAsync(locations[0].Id, locations[1].Id)], homeScope: true, home: locations[2].Id);

        Assert.Equal(locations.Select(l => l.Id).Order(), (await VisibleLocationIdsAsync(token)).Order());
    }

    // --- Gültigkeit (Vertretung / Zweitmeinung) -------------------------------------------------------------------------------

    [Fact]
    public async Task Validity_AnExpiredOrNotYetValidAssignmentGrantsNothing()
    {
        var a = await CreateLocationAsync(archived: true);
        var scope = await ScopeOfAsync(a.Id);
        var (_, valid) = await ScopedUserAsync(["location:read"], [scope], validFrom: Past, validTo: Future);
        var (_, expired) = await ScopedUserAsync(["location:read"], [scope], validFrom: Past.AddDays(-5), validTo: Past);
        var (_, future) = await ScopedUserAsync(["location:read"], [scope], validFrom: Future);

        Assert.Equal(HttpStatusCode.OK, (await GetAsync("/api/locations/archived", valid)).StatusCode);
        var expiredResponse = await GetAsync("/api/locations/archived", expired);
        Assert.Equal(HttpStatusCode.Forbidden, expiredResponse.StatusCode);
        Assert.Equal(AuthMessages.Read, await MessageAsync(expiredResponse));
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync("/api/locations/archived", future)).StatusCode);
    }

    [Fact]
    public async Task Validity_AlsoAppliesToUnscopedAssignments_AndShowsInMyPrivileges()
    {
        var (_, adminToken) = await AdminWithTokenAsync();
        var user = await CreateAdminAsync(withAdminRole: false);
        var role = await RoleAsync("location:read");
        var token = await LoginAsync(user.Username);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync("/api/locations/archived", token)).StatusCode);

        var assign = await SendAsync(HttpMethod.Put, $"/api/users/{user.Id}/roles",
            new { assignments = new[] { new { roleId = role, validFrom = Past, validTo = Future } } }, adminToken);

        Assert.Equal(HttpStatusCode.OK, assign.StatusCode);
        var row = (await DataAsync(assign)).EnumerateArray().Single();
        Assert.True(row.GetProperty("valid").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, row.GetProperty("validTo").ValueKind);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync("/api/locations/archived", token)).StatusCode);
        var mine = await DataAsync(await GetAsync("/api/me/privileges", token));
        Assert.Contains("location:read", mine.GetProperty("privileges").EnumerateArray().Select(p => p.GetString()));
        Assert.Equal(role, mine.GetProperty("roles")[0].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Validity_AnExpiredAssignmentStaysListedButInvalid_AndDoesNotAppearInMyPrivileges()
    {
        var (user, token) = await ScopedUserAsync(["location:read"], [], validTo: Past);
        var (_, admin) = await AdminWithTokenAsync();

        var listed = (await DataAsync(await GetAsync($"/api/users/{user.Id}/roles", admin))).EnumerateArray().Single();
        var mine = await DataAsync(await GetAsync("/api/me/privileges", token));

        Assert.False(listed.GetProperty("valid").GetBoolean());
        Assert.Empty(mine.GetProperty("roles").EnumerateArray());
        Assert.Empty(mine.GetProperty("privileges").EnumerateArray());
    }

    [Fact]
    public async Task MyPrivileges_ShowsTheScopePerPrivilege()
    {
        var a = await CreateLocationAsync();
        var (_, scoped) = await ScopedUserAsync(["location:read"], [await ScopeOfAsync(a.Id)]);
        var (_, admin) = await AdminWithTokenAsync();

        var mine = (await DataAsync(await GetAsync("/api/me/privileges", scoped))).GetProperty("scopes").GetProperty("location:read");
        var all = (await DataAsync(await GetAsync("/api/me/privileges", admin))).GetProperty("scopes").GetProperty("location:read");

        Assert.False(mine.GetProperty("all").GetBoolean());
        Assert.Equal([a.Id], mine.GetProperty("locationIds").EnumerateArray().Select(l => l.GetGuid()));
        Assert.True(all.GetProperty("all").GetBoolean());
    }

    // --- Nicht scopbare Rechte ---------------------------------------------------------------------------------------

    [Fact]
    public async Task ResourcesWithoutScopeSupport_IgnoreScopedAssignments()
    {
        var a = await CreateLocationAsync();
        var scope = await ScopeOfAsync(a.Id);
        var (_, token) = await ScopedUserAsync(["role:create", "custom_privilege:read", "scope:create"], [scope]);

        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync("/api/roles", new { name = Unique("R"), privileges = Array.Empty<string>() }, token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync("/api/custom-privileges", token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync("/api/scopes", new { name = Unique("R") }, token)).StatusCode);
    }

    // --- Filialen und Benutzer ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Locations_ScopedWritesOnlyInsideTheScope_AndNeverCreate()
    {
        var (a, b) = (await CreateLocationAsync(), await CreateLocationAsync());
        var (_, token) = await ScopedUserAsync(["location:create", "location:update", "location:delete", "location:read"], [await ScopeOfAsync(a.Id)]);

        Assert.Equal(HttpStatusCode.OK, (await PatchAsync($"/api/locations/{a.Id}", new { name = Unique("Neu") }, token)).StatusCode);
        var outside = await PatchAsync($"/api/locations/{b.Id}", new { name = "nein" }, token);
        Assert.Equal(HttpStatusCode.Forbidden, outside.StatusCode);
        Assert.Equal(AuthMessages.Update, await MessageAsync(outside));
        Assert.Equal(AuthMessages.Delete, await MessageAsync(await DeleteAsync($"/api/locations/{b.Id}", token)));
        var create = await PostAsync("/api/locations", new { name = Unique("X"), storeCode = Digits(9) }, token);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(AuthMessages.Create, await MessageAsync(create));
    }

    [Fact]
    public async Task Users_AScopedManagerSeesUsersOfHisLocations_ByHomeLocationOrScopedAssignment()
    {
        var (a, b) = (await CreateLocationAsync(), await CreateLocationAsync());
        var scopeA = await ScopeOfAsync(a.Id);
        var homeA = await CreateAdminAsync(withAdminRole: false, configure: u => u.HomeLocationId = a.Id);
        var homeB = await CreateAdminAsync(withAdminRole: false, configure: u => u.HomeLocationId = b.Id);
        var (viaScope, _) = await ScopedUserAsync(["location:read"], [scopeA]);
        var (_, manager) = await ScopedUserAsync(["user:read", "user:update"], [scopeA]);

        var ids = (await DataAsync(await GetAsync("/api/users?limit=500", manager))).GetProperty("items").EnumerateArray()
            .Select(u => u.GetProperty("id").GetGuid()).ToList();

        Assert.Contains(homeA.Id, ids);
        Assert.Contains(viaScope.Id, ids);
        Assert.DoesNotContain(homeB.Id, ids);
        Assert.Equal(HttpStatusCode.OK, (await PatchAsync($"/api/users/{homeA.Id}", new { active = true }, manager)).StatusCode);
        var outside = await PatchAsync($"/api/users/{homeB.Id}", new { active = true }, manager);
        Assert.Equal(HttpStatusCode.Forbidden, outside.StatusCode);
        Assert.Equal(AuthMessages.Update, await MessageAsync(outside));
        Assert.Equal(HttpStatusCode.NotFound, (await PatchAsync($"/api/users/{Guid.NewGuid()}", new { active = true }, manager)).StatusCode);
    }

    [Fact]
    public async Task Users_HomeLocationCanBeSetAndCleared_ButOnlyWithinTheActorsScope()
    {
        var (a, b) = (await CreateLocationAsync(), await CreateLocationAsync());
        var (_, admin) = await AdminWithTokenAsync();
        var target = await CreateAdminAsync(withAdminRole: false);

        var set = await SendAsync(HttpMethod.Put, $"/api/users/{target.Id}/home-location", new { locationId = a.Id }, admin);
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        Assert.Equal(a.Id, await QueryDbAsync(db => db.Users.Where(u => u.Id == target.Id).Select(u => u.HomeLocationId).SingleAsync()));

        var (_, manager) = await ScopedUserAsync(["user:update"], [await ScopeOfAsync(a.Id)]);
        var toOutside = await SendAsync(HttpMethod.Put, $"/api/users/{target.Id}/home-location", new { locationId = b.Id }, manager);
        Assert.Equal(HttpStatusCode.Forbidden, toOutside.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await SendAsync(HttpMethod.Put, $"/api/users/{target.Id}/home-location", new { locationId = Guid.NewGuid() }, admin)).StatusCode);

        var cleared = await SendAsync(HttpMethod.Put, $"/api/users/{target.Id}/home-location", new { locationId = (Guid?)null }, admin);
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        Assert.Null(await QueryDbAsync(db => db.Users.Where(u => u.Id == target.Id).Select(u => u.HomeLocationId).SingleAsync()));
    }

    // --- Delegation: nie mehr Reichweite vergeben als die eigene ----------------------------------------------------------

    [Fact]
    public async Task Delegation_ARegionalManagerAssignsOnlyWithinHisOwnScope()
    {
        var (a, b) = (await CreateLocationAsync(archived: true), await CreateLocationAsync(archived: true));
        var (scopeA, scopeB) = (await ScopeOfAsync(a.Id), await ScopeOfAsync(b.Id));
        var (_, manager) = await ScopedUserAsync(["role:assign", "role:read", "user:read", "location:read"], [scopeA]);
        var deputy = await CreateAdminAsync(withAdminRole: false, configure: u => u.HomeLocationId = a.Id);
        var readRole = await RoleAsync("location:read");

        // Im eigenen Scope, befristet: erlaubt (Urlaubsvertretung).
        var within = await SendAsync(HttpMethod.Put, $"/api/users/{deputy.Id}/roles",
            new { assignments = new[] { new { roleId = readRole, scoped = true, scopeIds = new[] { scopeA }, validTo = Future } } }, manager);
        Assert.Equal(HttpStatusCode.OK, within.StatusCode);

        // Anderer Scope, uneingeschränkt oder eine Rolle mit mehr Rechten: verboten.
        var otherScope = await SendAsync(HttpMethod.Put, $"/api/users/{deputy.Id}/roles",
            new { assignments = new[] { new { roleId = readRole, scoped = true, scopeIds = new[] { scopeB } } } }, manager);
        Assert.Equal(HttpStatusCode.Forbidden, otherScope.StatusCode);
        Assert.Equal(AclGuard.NoEscalation, await MessageAsync(otherScope));
        var unscoped = await SendAsync(HttpMethod.Put, $"/api/users/{deputy.Id}/roles", new { roleIds = new[] { readRole } }, manager);
        Assert.Equal(HttpStatusCode.Forbidden, unscoped.StatusCode);
        var bigger = await RoleAsync("location:read", "location:delete");
        var tooMuch = await SendAsync(HttpMethod.Put, $"/api/users/{deputy.Id}/roles",
            new { assignments = new[] { new { roleId = bigger, scoped = true, scopeIds = new[] { scopeA } } } }, manager);
        Assert.Equal(HttpStatusCode.Forbidden, tooMuch.StatusCode);

        // Der Vertreter sieht nur die Filialen des Scopes A.
        var deputyToken = await LoginAsync(deputy.Username);
        Assert.Equal([a.Id], await VisibleLocationIdsAsync(deputyToken));
    }

    [Fact]
    public async Task Delegation_CannotOutlastTheDelegatorsOwnAssignment()
    {
        var a = await CreateLocationAsync();
        var scopeA = await ScopeOfAsync(a.Id);
        var readRole = await RoleAsync("location:read");
        var limit = DateTime.UtcNow.AddDays(3);
        var (_, temporary) = await ScopedUserAsync(["role:assign", "location:read", "user:read"], [scopeA], validTo: limit);
        var (_, permanent) = await ScopedUserAsync(["role:assign", "location:read", "user:read"], [scopeA]);
        var target = await CreateAdminAsync(withAdminRole: false, configure: u => u.HomeLocationId = a.Id);

        Task<HttpResponseMessage> AssignAsync(string token, DateTime? validTo) =>
            SendAsync(HttpMethod.Put, $"/api/users/{target.Id}/roles",
                new { assignments = new[] { new { roleId = readRole, scoped = true, scopeIds = new[] { scopeA }, validTo } } }, token);

        // Befristet: nicht unbefristet und nicht länger als die eigene Zuweisung.
        var unlimited = await AssignAsync(temporary, null);
        Assert.Equal(HttpStatusCode.Forbidden, unlimited.StatusCode);
        Assert.Equal(AclGuard.ValidityTooLong, await MessageAsync(unlimited));
        Assert.Equal(HttpStatusCode.Forbidden, (await AssignAsync(temporary, limit.AddDays(1))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await AssignAsync(temporary, limit.AddHours(-1))).StatusCode);

        // Unbefristet darf unbefristet und beliebig befristet weitergeben.
        Assert.Equal(HttpStatusCode.OK, (await AssignAsync(permanent, null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await AssignAsync(permanent, DateTime.UtcNow.AddDays(400))).StatusCode);
    }

    [Fact]
    public async Task Delegation_AUserOutsideTheManagersScopeIsOutOfReach()
    {
        var (a, b) = (await CreateLocationAsync(), await CreateLocationAsync());
        var (_, manager) = await ScopedUserAsync(["role:assign"], [await ScopeOfAsync(a.Id)]);
        var stranger = await CreateAdminAsync(withAdminRole: false, configure: u => u.HomeLocationId = b.Id);

        var response = await SendAsync(HttpMethod.Put, $"/api/users/{stranger.Id}/roles", new { roleIds = Array.Empty<Guid>() }, manager);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Assignments_AreValidated()
    {
        var (_, admin) = await AdminWithTokenAsync();
        var user = await CreateAdminAsync(withAdminRole: false);
        var role = await RoleAsync("location:read");
        var scope = await ScopeOfAsync((await CreateLocationAsync()).Id);

        async Task<HttpStatusCode> PutAsync(object body) =>
            (await SendAsync(HttpMethod.Put, $"/api/users/{user.Id}/roles", body, admin)).StatusCode;

        Assert.Equal(HttpStatusCode.UnprocessableEntity, await PutAsync(new { }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await PutAsync(new { roleIds = new[] { role }, assignments = new[] { new { roleId = role } } }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await PutAsync(new { assignments = new[] { new { roleId = role }, new { roleId = role } } }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await PutAsync(new { assignments = new[] { new { roleId = role, scoped = false, scopeIds = new[] { scope } } } }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await PutAsync(new { assignments = new[] { new { roleId = role, scoped = false, homeScope = true } } }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await PutAsync(new { assignments = new[] { new { roleId = role, scoped = true, scopeIds = new[] { Guid.NewGuid() } } } }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await PutAsync(new { assignments = new[] { new { roleId = role, validFrom = Future, validTo = Past } } }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await PutAsync(new { assignments = new[] { new { roleId = Guid.NewGuid() } } }));
        Assert.Equal(HttpStatusCode.OK, await PutAsync(new { assignments = new[] { new { roleId = role, scoped = true, scopeIds = new[] { scope } } } }));
    }

    [Fact]
    public async Task Assignments_ReplaceScopesAndSurviveUpdates()
    {
        var (_, admin) = await AdminWithTokenAsync();
        var user = await CreateAdminAsync(withAdminRole: false);
        var (s1, s2) = (await ScopeOfAsync((await CreateLocationAsync()).Id), await ScopeOfAsync((await CreateLocationAsync()).Id));
        var role = await RoleAsync("location:read");

        await SendAsync(HttpMethod.Put, $"/api/users/{user.Id}/roles", new { assignments = new[] { new { roleId = role, scoped = true, scopeIds = new[] { s1 } } } }, admin);
        var second = await SendAsync(HttpMethod.Put, $"/api/users/{user.Id}/roles", new { assignments = new[] { new { roleId = role, scoped = true, scopeIds = new[] { s2 } } } }, admin);

        var row = (await DataAsync(second)).EnumerateArray().Single();
        Assert.Equal([s2], row.GetProperty("scopes").EnumerateArray().Select(s => s.GetProperty("id").GetGuid()));
        Assert.True(row.GetProperty("scoped").GetBoolean());
        Assert.Equal(1, await QueryDbAsync(db => db.AclUserRoleScopes.CountAsync(s => s.UserId == user.Id)));
    }

    // --- Scope-Verwaltung ------------------------------------------------------------------------------------------

    [Fact]
    public async Task Scopes_CanBeManagedInTheUi_WithLocationAndReportMembers()
    {
        var (_, admin) = await AdminWithTokenAsync();
        var location = await CreateLocationAsync();
        var reportId = Guid.NewGuid(); // Einzelmeldungen führt das Portal; dieser Dienst speichert die ID ohne Prüfung
        var name = Unique("Region");

        var created = await PostAsync("/api/scopes", new
        {
            name,
            description = " Nord ",
            members = new object[] { new { dimension = "location", refId = location.Id }, new { dimension = "report", refId = reportId }, new { dimension = "location", refId = location.Id } }
        }, admin);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var data = await DataAsync(created);
        Assert.Equal("Nord", data.GetProperty("description").GetString());
        Assert.Equal(2, data.GetProperty("members").GetArrayLength());
        var id = data.GetProperty("id").GetGuid();

        var renamed = await PatchAsync($"/api/scopes/{id}", new { name = Unique("Region"), members = new[] { new { dimension = "location", refId = location.Id } } }, admin);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal(1, (await DataAsync(renamed)).GetProperty("members").GetArrayLength());
        Assert.Equal(HttpStatusCode.OK, (await GetAsync($"/api/scopes/{id}", admin)).StatusCode);
        Assert.Contains((await DataAsync(await GetAsync("/api/scopes", admin))).EnumerateArray(), s => s.GetProperty("id").GetGuid() == id);
        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync($"/api/scopes/{id}", admin)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync($"/api/scopes/{id}", admin)).StatusCode);
    }

    [Fact]
    public async Task Scopes_AreValidated_AndNamesStayUnique()
    {
        var (_, admin) = await AdminWithTokenAsync();
        var name = Unique("Region");
        Assert.Equal(HttpStatusCode.Created, (await PostAsync("/api/scopes", new { name }, admin)).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync("/api/scopes", new { name }, admin)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await PostAsync("/api/scopes", new { name = "  " }, admin)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await PostAsync("/api/scopes", new { description = "x" }, admin)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await PostAsync("/api/scopes", new { name = Unique("R"), members = new[] { new { dimension = "abteilung", refId = Guid.NewGuid() } } }, admin)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await PostAsync("/api/scopes", new { name = Unique("R"), members = new[] { new { dimension = "location", refId = Guid.NewGuid() } } }, admin)).StatusCode);
    }

    [Fact]
    public async Task Scopes_CannotBeDeletedWhileAssigned()
    {
        var (_, admin) = await AdminWithTokenAsync();
        var scope = await ScopeOfAsync((await CreateLocationAsync()).Id);
        var (user, _) = await ScopedUserAsync(["location:read"], [scope]);

        Assert.Equal(HttpStatusCode.Conflict, (await DeleteAsync($"/api/scopes/{scope}", admin)).StatusCode);

        await Factory.WithDbAsync(async (db, _) => await db.AclUserRoles.Where(x => x.UserId == user.Id).ExecuteDeleteAsync());
        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync($"/api/scopes/{scope}", admin)).StatusCode);
    }

    [Fact]
    public async Task Scopes_AScopedReaderSeesOnlyScopesInsideHisReach()
    {
        var (a, b) = (await CreateLocationAsync(), await CreateLocationAsync());
        var (inside, outside) = (await ScopeOfAsync(a.Id), await ScopeOfAsync(a.Id, b.Id));
        var (_, token) = await ScopedUserAsync(["scope:read"], [inside]);

        var ids = (await DataAsync(await GetAsync("/api/scopes", token))).EnumerateArray().Select(s => s.GetProperty("id").GetGuid()).ToList();

        Assert.Contains(inside, ids);
        Assert.DoesNotContain(outside, ids);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync($"/api/scopes/{outside}", token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync("/api/scopes", new { name = Unique("R") }, token)).StatusCode);
    }

    // --- Administratoren ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ARestrictedWildcardAssignment_IsNotAnAdministrator()
    {
        var (admin, _) = await AdminWithTokenAsync();
        var a = await CreateLocationAsync();
        var (scopedAll, _) = await ScopedUserAsync(["*"], [await ScopeOfAsync(a.Id)]);
        var (temporary, token) = await TemporaryAdminWithTokenAsync();
        await Factory.WithDbAsync(async (db, _) =>
            await db.Users.Where(u => u.Id != admin.Id && u.Id != temporary.Id && u.Id != scopedAll.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.Active, false)));

        // Der dauerhafte, uneingeschränkte Administrator ist der letzte: weder das eingeschränkte "*" noch die befristete Zuweisung zählen.
        var response = await PatchAsync($"/api/users/{admin.Id}", new { active = false }, token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(AclGuard.LastAdministrator, await MessageAsync(response));
    }
}
