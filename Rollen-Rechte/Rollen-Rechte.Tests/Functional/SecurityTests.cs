using System.Net;
using System.Text.Json;
using Rollen_Rechte.Authentication;
using Rollen_Rechte.Domain.Entities;
using Rollen_Rechte.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Rollen_Rechte.Tests.Functional;

/// <summary>Regressionstests aus dem Sicherheits-Review: Wege, über die sich jemand mehr Rechte oder Reichweite hätte verschaffen können.</summary>
public class SecurityTests(ApiFactory factory) : ApiTestBase(factory)
{
    private static readonly DateTime Future = DateTime.UtcNow.AddDays(2);

    private Task<Guid> RoleAsync(params string[] privileges) =>
        Factory.WithDbAsync(async (db, _) =>
        {
            var role = new AclRole { Name = Unique("Rolle"), Privileges = [.. privileges] };
            db.AclRoles.Add(role);
            await db.SaveChangesAsync();
            return role.Id;
        });

    private Task<Guid> ScopeAsync(params Guid[] locationIds) =>
        Factory.WithDbAsync(async (db, _) =>
        {
            var scope = new Scope { Name = Unique("Scope"), Members = [.. locationIds.Select(id => new ScopeMember { Dimension = ScopeDimensions.Location, RefId = id })] };
            db.Scopes.Add(scope);
            await db.SaveChangesAsync();
            return scope.Id;
        });

    /// <summary>User mit mehreren Zuweisungen: je Eintrag die Rechte der Rolle; mit <c>scopeIds</c> eingeschränkt, ohne uneingeschränkt.</summary>
    private async Task<(User User, string Token)> UserWithAsync(params (string[] Privileges, Guid[]? ScopeIds)[] assignments)
    {
        var user = await CreateAdminAsync(withAdminRole: false);
        await Factory.WithDbAsync(async (db, _) =>
        {
            foreach (var (privileges, scopeIds) in assignments)
            {
                var role = new AclRole { Name = Unique("Rolle"), Privileges = [.. privileges] };
                db.AclRoles.Add(role);
                db.AclUserRoles.Add(new AclUserRole
                {
                    UserId = user.Id, AclRoleId = role.Id, Scoped = scopeIds is not null,
                    Scopes = [.. (scopeIds ?? []).Select(sid => new AclUserRoleScope { UserId = user.Id, AclRoleId = role.Id, ScopeId = sid })]
                });
            }

            await db.SaveChangesAsync();
        });
        return (user, await LoginAsync(user.Username));
    }

    private static async Task<string?> MessageAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("message").GetString();

    // --- Account-Übernahme: nie einen Stärkeren verändern -----------------------------------------------------------

    [Fact]
    public async Task ResetPassword_OfAStrongerUser_IsForbidden_SoNobodyTakesOverAnAdminAccount()
    {
        var (admin, _) = await AdminWithTokenAsync();
        var (_, manager) = await UserWithAsync((["user:read", "user:update"], null));
        var weaker = await CreateAdminAsync(withAdminRole: false);

        var takeover = await PatchAsync($"/api/users/{admin.Id}/password", new { password = "neues-passwort-123" }, manager);

        Assert.Equal(HttpStatusCode.Forbidden, takeover.StatusCode);
        Assert.Equal(AclGuard.NoEscalation, await MessageAsync(takeover));
        // Der Admin kann sich weiterhin mit dem alten Passwort anmelden.
        Assert.False(string.IsNullOrEmpty(await LoginAsync(admin.Username)));
        // Einen User ohne Rechte darf er zurücksetzen.
        Assert.Equal(HttpStatusCode.OK, (await PatchAsync($"/api/users/{weaker.Id}/password", new { password = "neues-passwort-123" }, manager)).StatusCode);
    }

    [Fact]
    public async Task ResetPassword_OfAUserWithEqualRights_IsAllowed()
    {
        var (_, manager) = await UserWithAsync((["user:update", "user:read"], null));
        var (peer, _) = await UserWithAsync((["user:read"], null));

        Assert.Equal(HttpStatusCode.OK, (await PatchAsync($"/api/users/{peer.Id}/password", new { password = "neues-passwort-123" }, manager)).StatusCode);
    }

    [Fact]
    public async Task Deactivate_Delete_AndHomeLocation_OfAStrongerUser_AreForbidden()
    {
        var (admin, _) = await AdminWithTokenAsync();
        var location = await CreateLocationAsync();
        var (_, manager) = await UserWithAsync((["user:update", "user:delete"], null));

        Assert.Equal(HttpStatusCode.Forbidden, (await PatchAsync($"/api/users/{admin.Id}", new { active = false }, manager)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await DeleteAsync($"/api/users/{admin.Id}", manager)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await SendAsync(HttpMethod.Put, $"/api/users/{admin.Id}/home-location", new { locationId = location.Id }, manager)).StatusCode);
        Assert.True(await QueryDbAsync(db => db.Users.Where(u => u.Id == admin.Id).Select(u => u.Active).SingleAsync()));
    }

    // --- Rollen: Rechte, die nur eingeschränkt wirken, dürfen nicht in unbeschränkte Rollen wandern ----------------------------------

    [Fact]
    public async Task EditingARole_NeedsTheRightsUnrestricted_ARestrictedRightDoesNotCount()
    {
        var a = await CreateLocationAsync();
        var scope = await ScopeAsync(a.Id);
        // Der Handelnde darf Rollen ändern und Meldungen lesen (uneingeschränkt), Meldungen ändern aber nur im Scope.
        var (_, actor) = await UserWithAsync((["role:read", "role:update", "user:read"], null), (["user:update"], [scope]));
        var shared = await RoleAsync("user:read"); // gilt für alle Inhaber, auch uneingeschränkte

        var escalate = await PatchAsync($"/api/roles/{shared}", new { privileges = new[] { "user:read", "user:update" } }, actor);

        Assert.Equal(HttpStatusCode.Forbidden, escalate.StatusCode);
        Assert.Equal(AclGuard.NoEscalation, await MessageAsync(escalate));
        Assert.Equal(["user:read"], await QueryDbAsync(db => db.AclRoles.Where(r => r.Id == shared).Select(r => r.Privileges).SingleAsync()));
    }

    [Fact]
    public async Task EditingARole_WithAnUnrestrictedRight_IsAllowed()
    {
        var (_, actor) = await UserWithAsync((["role:read", "role:update", "user:read", "user:update"], null));
        var shared = await RoleAsync("user:read");

        var response = await PatchAsync($"/api/roles/{shared}", new { privileges = new[] { "user:read", "user:update" } }, actor);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RoleContent_CannotCarryARightThatOnlyWorksInAnInertRestrictedAssignment()
    {
        var scope = await ScopeAsync((await CreateLocationAsync()).Id);
        // "custom_privilege:create" steht nur in einer eingeschränkten Zuweisung und wirkt dort gar nicht (eigene Rechte sind nicht scopbar).
        var (_, actor) = await UserWithAsync((["role:create", "role:read"], null), (["custom_privilege:create"], [scope]));

        var response = await PostAsync("/api/roles", new { name = Unique("Rolle"), privileges = new[] { "custom_privilege:create" } }, actor);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- Scopes: die eigene Reichweite nicht selbst vergrößern --------------------------------------------------------------

    [Fact]
    public async Task Scopes_CannotBeWidenedBeyondTheEditorsOwnReach()
    {
        var (a, b) = (await CreateLocationAsync(), await CreateLocationAsync());
        var scope = await ScopeAsync(a.Id);
        // Darf Scopes verwalten (uneingeschränkt), liest Meldungen aber nur im Scope.
        var (_, editor) = await UserWithAsync((["scope:read", "scope:create", "scope:update", "scope:delete"], null), (["user:read"], [scope]));
        var members = new[] { new { dimension = "location", refId = a.Id }, new { dimension = "location", refId = b.Id } };

        var widen = await PatchAsync($"/api/scopes/{scope}", new { members }, editor);
        Assert.Equal(HttpStatusCode.Forbidden, widen.StatusCode);
        Assert.Equal(AclGuard.NoEscalation, await MessageAsync(widen));
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync("/api/scopes", new { name = Unique("Neu"), members }, editor)).StatusCode);

        Assert.Equal(HttpStatusCode.Created,
            (await PostAsync("/api/scopes", new { name = Unique("Eng"), members = new[] { new { dimension = "location", refId = a.Id } } }, editor)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PatchAsync($"/api/scopes/{scope}", new { description = "nur Text" }, editor)).StatusCode);
        Assert.Equal(1, await QueryDbAsync(db => db.ScopeMembers.CountAsync(m => m.ScopeId == scope)));
    }

    [Fact]
    public async Task Scopes_CannotBeDeletedByAnEditorWhoDoesNotCoverTheirMembers()
    {
        var (a, b) = (await CreateLocationAsync(), await CreateLocationAsync());
        var mine = await ScopeAsync(a.Id);
        var theirs = await ScopeAsync(b.Id);
        var (_, editor) = await UserWithAsync((["scope:read", "scope:delete"], null), (["user:read"], [mine]));

        Assert.Equal(HttpStatusCode.Forbidden, (await DeleteAsync($"/api/scopes/{theirs}", editor)).StatusCode);
        // Der eigene Scope ist dem Editor selbst zugewiesen und lässt sich deshalb nicht löschen (409), aber er ist nicht gesperrt.
        Assert.Equal(HttpStatusCode.Conflict, (await DeleteAsync($"/api/scopes/{mine}", editor)).StatusCode);
    }

    // --- Administratoren: befristete zählen nicht ------------------------------------------------------------------

    [Fact]
    public async Task ATemporaryAdministratorAssignment_DoesNotCountAsStayingAdministrator()
    {
        var (admin, token) = await AdminWithTokenAsync();
        var (other, _) = await TemporaryAdminWithTokenAsync();
        await Factory.WithDbAsync(async (db, _) =>
            await db.Users.Where(u => u.Id != admin.Id && u.Id != other.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.Active, false)));
        var adminRole = await Factory.WithDbAsync((db, _) => db.AclRoles.Where(r => r.Name == "Administrator").Select(r => r.Id).SingleAsync());

        // Der einzige dauerhafte Administrator will sich selbst nur befristet den Administrator geben: danach gäbe es keinen mehr.
        var response = await SendAsync(HttpMethod.Put, $"/api/users/{admin.Id}/roles",
            new { assignments = new[] { new { roleId = adminRole, validTo = Future } } }, token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(AclGuard.LastAdministrator, await MessageAsync(response));
    }

    // --- Eingebaute Rolle: auch in anderer Schreibweise nicht anlegbar ------------------------------------------------

    [Theory]
    [InlineData("administrator")]
    [InlineData("ADMINISTRATOR")]
    [InlineData("  Administrator  ")]
    public async Task TheBuiltInRoleName_CannotBeTakenInAnotherSpelling(string name)
    {
        var (_, token) = await AdminWithTokenAsync();

        var response = await PostAsync("/api/roles", new { name, privileges = Array.Empty<string>() }, token);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task UserResponses_ShowTheHomeLocation()
    {
        var (_, admin) = await AdminWithTokenAsync();
        var location = await CreateLocationAsync();
        var user = await CreateAdminAsync(withAdminRole: false, configure: u => u.HomeLocationId = location.Id);

        var users = (await DataAsync(await GetAsync("/api/users?limit=500", admin))).GetProperty("items").EnumerateArray();

        Assert.Equal(location.Id, users.Single(u => u.GetProperty("id").GetGuid() == user.Id).GetProperty("homeLocationId").GetGuid());
    }}
