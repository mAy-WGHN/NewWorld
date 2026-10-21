using System.Net;
using Rollen_Rechte.Authentication;
using Rollen_Rechte.Domain.Entities;
using Rollen_Rechte.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Rollen_Rechte.Tests.Functional;

public class RoleApiTests(ApiFactory factory) : ApiTestBase(factory)
{
    private static readonly string[] Manager = ["role:read", "role:create", "role:update", "role:delete", "role:assign"];

    private Task<Guid> CreateRoleAsync(string? name = null, params string[] privileges) =>
        Factory.WithDbAsync(async (db, _) =>
        {
            var role = new AclRole { Name = name ?? Unique("Rolle"), Privileges = [.. privileges] };
            db.AclRoles.Add(role);
            await db.SaveChangesAsync();
            return role.Id;
        });

    private Task AssignAsync(Guid userId, Guid roleId) =>
        Factory.WithDbAsync(async (db, _) =>
        {
            db.AclUserRoles.Add(new AclUserRole { UserId = userId, AclRoleId = roleId });
            await db.SaveChangesAsync();
        });

    /// <summary>Macht alle anderen User inaktiv: Für Tests der "letzter Administrator"-Regel (die Tabellen sind sonst von anderen Tests gefüllt).</summary>
    private Task DeactivateAllExceptAsync(params Guid[] keep) =>
        Factory.WithDbAsync(async (db, _) =>
            await db.Users.Where(u => !keep.Contains(u.Id)).ExecuteUpdateAsync(s => s.SetProperty(u => u.Active, false)));

    private static string[] Privs(System.Text.Json.JsonElement role) =>
        role.GetProperty("privileges").EnumerateArray().Select(p => p.GetString()!).ToArray();

    // --- Katalog und eigene Rechte ----------------------------------------------------------------------------

    [Fact]
    public async Task Catalog_ListsAreasWithLabelsAndActions()
    {
        var (_, token) = await UserWithPrivilegesAsync("role:read");

        var response = await GetAsync("/api/privileges", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var report = (await DataAsync(response)).EnumerateArray().Single(a => a.GetProperty("key").GetString() == "user");
        Assert.Equal("Benutzer", report.GetProperty("label").GetString());
        var update = report.GetProperty("actions").EnumerateArray().Single(a => a.GetProperty("privilege").GetString() == "user:update");
        Assert.Equal("update", update.GetProperty("action").GetString());
        Assert.False(string.IsNullOrEmpty(update.GetProperty("label").GetString()));
    }

    [Fact]
    public async Task MyPrivileges_ExpandsWildcards_AndListsTheRoles()
    {
        var (_, admin) = await AdminWithTokenAsync();
        var (_, partial) = await UserWithPrivilegesAsync("user:*");
        var (_, none) = await UserWithPrivilegesAsync();

        var all = await DataAsync(await GetAsync("/api/me/privileges", admin));
        var snapshot = await Factory.Services.GetRequiredService<PrivilegeCatalogService>().GetAsync();
        Assert.Equal(snapshot.All.Order(StringComparer.Ordinal), all.GetProperty("privileges").EnumerateArray().Select(p => p.GetString()!));
        Assert.Equal("Administrator", all.GetProperty("roles")[0].GetProperty("name").GetString());

        var report = await DataAsync(await GetAsync("/api/me/privileges", partial));
        Assert.Equal(["user:create", "user:delete", "user:read", "user:update"], report.GetProperty("privileges").EnumerateArray().Select(p => p.GetString()!));

        var nothing = await DataAsync(await GetAsync("/api/me/privileges", none));
        Assert.Empty(nothing.GetProperty("privileges").EnumerateArray());
    }

    // --- Rollen anlegen / ändern / löschen ------------------------------------------------------------------

    [Fact]
    public async Task Create_StoresTrimmedSortedDistinctPrivileges()
    {
        var (_, token) = await AdminWithTokenAsync();
        var name = Unique("Sachbearbeiter");

        var response = await PostAsync("/api/roles",
            new { name = $"  {name} ", description = " Darf Meldungen bearbeiten ", privileges = new[] { "user:update", "user:read", " user:read ", "" } }, token);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var role = await DataAsync(response);
        Assert.Equal(name, role.GetProperty("name").GetString());
        Assert.Equal("Darf Meldungen bearbeiten", role.GetProperty("description").GetString());
        Assert.Equal(["user:read", "user:update"], Privs(role));
        Assert.False(role.GetProperty("builtIn").GetBoolean());
        Assert.Equal(0, role.GetProperty("userCount").GetInt32());
    }

    [Theory]
    [InlineData("report:erfinden")]
    [InlineData("unbekannt:*")]
    public async Task Create_WithUnknownPrivilege_Returns422(string privilege)
    {
        var (_, token) = await AdminWithTokenAsync();

        var response = await PostAsync("/api/roles", new { name = Unique("Rolle"), privileges = new[] { privilege } }, token);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("privileges", (await JsonAsync(response)).GetProperty("errors")[0].GetProperty("field").GetString());
    }
    [Fact]
    public async Task Create_WithWildcardsOfKnownAreas_IsAccepted()
    {
        var (_, token) = await AdminWithTokenAsync();

        var response = await PostAsync("/api/roles", new { name = Unique("Rolle"), privileges = new[] { "user:*", "location:read" } }, token);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_ValidatesNameAndPrivilegesAndRejectsDuplicates()
    {
        var (_, token) = await AdminWithTokenAsync();
        var name = Unique("Rolle");
        Assert.Equal(HttpStatusCode.Created, (await PostAsync("/api/roles", new { name, privileges = Array.Empty<string>() }, token)).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync("/api/roles", new { name, privileges = Array.Empty<string>() }, token)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync("/api/roles", new { name = "Administrator", privileges = Array.Empty<string>() }, token)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await PostAsync("/api/roles", new { name = "  ", privileges = Array.Empty<string>() }, token)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await PostAsync("/api/roles", new { privileges = Array.Empty<string>() }, token)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await PostAsync("/api/roles", new { name = Unique("Rolle") }, token)).StatusCode);
    }

    [Fact]
    public async Task Update_ChangesNameDescriptionAndPrivileges_PartiallyAndNamesStayUnique()
    {
        var (_, token) = await AdminWithTokenAsync();
        var id = await CreateRoleAsync(null, "user:read");
        var other = Unique("Andere");
        await CreateRoleAsync(other);

        var renamed = await PatchAsync($"/api/roles/{id}", new { name = "Neu-" + Guid.NewGuid().ToString("N")[..6] }, token);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal(["user:read"], Privs(await DataAsync(renamed))); // Rechte unverändert

        var changed = await PatchAsync($"/api/roles/{id}", new { description = "Text", privileges = new[] { "location:read" } }, token);
        Assert.Equal(["location:read"], Privs(await DataAsync(changed)));

        Assert.Equal(HttpStatusCode.Conflict, (await PatchAsync($"/api/roles/{id}", new { name = other }, token)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await PatchAsync($"/api/roles/{id}", new { privileges = new[] { "nope:nope" } }, token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PatchAsync($"/api/roles/{Guid.NewGuid()}", new { name = "x" }, token)).StatusCode);
    }

    [Fact]
    public async Task List_ShowsUserCountsAndTheBuiltInFlag()
    {
        var (_, token) = await AdminWithTokenAsync();
        var user = await CreateAdminAsync(withAdminRole: false);
        var id = await CreateRoleAsync(null, "user:read");
        await AssignAsync(user.Id, id);

        var roles = (await DataAsync(await GetAsync("/api/roles", token))).EnumerateArray().ToList();

        Assert.Equal(1, roles.Single(r => r.GetProperty("id").GetGuid() == id).GetProperty("userCount").GetInt32());
        Assert.True(roles.Single(r => r.GetProperty("name").GetString() == "Administrator").GetProperty("builtIn").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await GetAsync($"/api/roles/{id}", token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync($"/api/roles/{Guid.NewGuid()}", token)).StatusCode);
    }

    [Fact]
    public async Task Delete_OnlyWithoutAssignments_AndNeverTheBuiltInRole()
    {
        var (_, token) = await AdminWithTokenAsync();
        var user = await CreateAdminAsync(withAdminRole: false);
        var used = await CreateRoleAsync(null, "user:read");
        await AssignAsync(user.Id, used);
        var unused = await CreateRoleAsync();
        var adminRoleId = await Factory.WithDbAsync((db, _) => db.AclRoles.Where(r => r.Name == "Administrator").Select(r => r.Id).SingleAsync());

        Assert.Equal(HttpStatusCode.Conflict, (await DeleteAsync($"/api/roles/{used}", token)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync($"/api/roles/{unused}", token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await DeleteAsync($"/api/roles/{unused}", token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await DeleteAsync($"/api/roles/{adminRoleId}", token)).StatusCode);
    }

    [Fact]
    public async Task BuiltInAdministratorRole_CannotBeChanged_NotEvenByAnAdministrator()
    {
        var (_, token) = await AdminWithTokenAsync();
        var adminRoleId = await Factory.WithDbAsync((db, _) => db.AclRoles.Where(r => r.Name == "Administrator").Select(r => r.Id).SingleAsync());

        var response = await PatchAsync($"/api/roles/{adminRoleId}", new { privileges = new[] { "user:read" } }, token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(AclGuard.BuiltInRole, (await JsonAsync(response)).GetProperty("message").GetString());
    }

    // --- Keine Rechte-Eskalation --------------------------------------------------------------------------------

    [Fact]
    public async Task Manager_CannotGrantPrivilegesHeDoesNotHave()
    {
        var (_, token) = await UserWithPrivilegesAsync([.. Manager, "user:read"]);

        Assert.Equal(HttpStatusCode.Created,
            (await PostAsync("/api/roles", new { name = Unique("Rolle"), privileges = new[] { "user:read" } }, token)).StatusCode);

        var tooMuch = await PostAsync("/api/roles", new { name = Unique("Rolle"), privileges = new[] { "user:read", "location:read" } }, token);
        Assert.Equal(HttpStatusCode.Forbidden, tooMuch.StatusCode);
        Assert.Equal(AclGuard.NoEscalation, (await JsonAsync(tooMuch)).GetProperty("message").GetString());

        // Platzhalter nur, wenn er sie selbst abdeckt: "user:read" deckt weder "user:*" noch "*" ab.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await PostAsync("/api/roles", new { name = Unique("Rolle"), privileges = new[] { "user:*" } }, token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await PostAsync("/api/roles", new { name = Unique("Rolle"), privileges = new[] { "*" } }, token)).StatusCode);
    }

    [Fact]
    public async Task Manager_WithAWildcard_MayGrantWhatItCovers()
    {
        var (_, token) = await UserWithPrivilegesAsync([.. Manager, "user:*"]);

        Assert.Equal(HttpStatusCode.Created,
            (await PostAsync("/api/roles", new { name = Unique("Rolle"), privileges = new[] { "user:*", "user:update" } }, token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await PostAsync("/api/roles", new { name = Unique("Rolle"), privileges = new[] { "location:read" } }, token)).StatusCode);
    }

    [Fact]
    public async Task Manager_CannotEditOrDeleteARoleThatCanDoMoreThanHe()
    {
        var (_, token) = await UserWithPrivilegesAsync([.. Manager, "user:read"]);
        var stronger = await CreateRoleAsync(null, "user:read", "user:delete");
        var weaker = await CreateRoleAsync(null, "user:read");

        Assert.Equal(HttpStatusCode.Forbidden, (await PatchAsync($"/api/roles/{stronger}", new { name = "Umbenannt" }, token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PatchAsync($"/api/roles/{stronger}", new { privileges = new[] { "user:read" } }, token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await DeleteAsync($"/api/roles/{stronger}", token)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await PatchAsync($"/api/roles/{weaker}", new { name = Unique("Umbenannt") }, token)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync($"/api/roles/{weaker}", token)).StatusCode);
    }

    // --- Rollen an User vergeben --------------------------------------------------------------------------------

    [Fact]
    public async Task SetRoles_ReplacesTheAssignments_AndTakesEffectImmediately()
    {
        var (_, admin) = await AdminWithTokenAsync();
        var (target, targetToken) = await UserWithPrivilegesAsync();
        var reportRole = await CreateRoleAsync(null, "user:read");
        var statisticRole = await CreateRoleAsync(null, "location:read");
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync("/api/users", targetToken)).StatusCode);

        var first = await SendAsync(HttpMethod.Put, $"/api/users/{target.Id}/roles", new { roleIds = new[] { reportRole, statisticRole } }, admin);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(2, (await DataAsync(first)).GetArrayLength());
        Assert.Equal(HttpStatusCode.OK, (await GetAsync("/api/users", targetToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync("/api/locations/archived", targetToken)).StatusCode);

        var second = await SendAsync(HttpMethod.Put, $"/api/users/{target.Id}/roles", new { roleIds = new[] { statisticRole, statisticRole } }, admin);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync("/api/users", targetToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync("/api/locations/archived", targetToken)).StatusCode);

        var current = (await DataAsync(await GetAsync($"/api/users/{target.Id}/roles", admin))).EnumerateArray().ToList();
        Assert.Equal(statisticRole, Assert.Single(current).GetProperty("id").GetGuid());

        Assert.Equal(HttpStatusCode.OK,
            (await SendAsync(HttpMethod.Put, $"/api/users/{target.Id}/roles", new { roleIds = Array.Empty<Guid>() }, admin)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync("/api/locations/archived", targetToken)).StatusCode);
    }

    [Fact]
    public async Task SetRoles_RejectsUnknownRolesAndUsers_AndMissingBody()
    {
        var (_, admin) = await AdminWithTokenAsync();
        var target = await CreateAdminAsync(withAdminRole: false);

        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await SendAsync(HttpMethod.Put, $"/api/users/{target.Id}/roles", new { roleIds = new[] { Guid.NewGuid() } }, admin)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await SendAsync(HttpMethod.Put, $"/api/users/{target.Id}/roles", new { }, admin)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await SendAsync(HttpMethod.Put, $"/api/users/{Guid.NewGuid()}/roles", new { roleIds = Array.Empty<Guid>() }, admin)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync($"/api/users/{Guid.NewGuid()}/roles", admin)).StatusCode);
    }

    [Fact]
    public async Task SetRoles_CannotAddOrRemoveARoleThatCanDoMoreThanTheActor()
    {
        var (_, token) = await UserWithPrivilegesAsync("role:assign", "user:read");
        var target = await CreateAdminAsync(withAdminRole: false);
        var weak = await CreateRoleAsync(null, "user:read");
        var strong = await CreateRoleAsync(null, "user:read", "user:delete");

        // Hinzufügen
        Assert.Equal(HttpStatusCode.Forbidden,
            (await SendAsync(HttpMethod.Put, $"/api/users/{target.Id}/roles", new { roleIds = new[] { strong } }, token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await SendAsync(HttpMethod.Put, $"/api/users/{target.Id}/roles", new { roleIds = new[] { weak } }, token)).StatusCode);

        // Entziehen einer stärkeren Rolle (der User hat sie, der Handelnde nicht abdeckt)
        await AssignAsync(target.Id, strong);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await SendAsync(HttpMethod.Put, $"/api/users/{target.Id}/roles", new { roleIds = new[] { weak } }, token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await SendAsync(HttpMethod.Put, $"/api/users/{target.Id}/roles", new { roleIds = new[] { weak, strong } }, token)).StatusCode); // unverändert = erlaubt
    }

    // --- Der letzte Administrator bleibt ------------------------------------------------------------------------

    [Fact]
    public async Task LastAdministrator_CannotLoseTheAdministratorRole()
    {
        var (admin, token) = await AdminWithTokenAsync();
        await DeactivateAllExceptAsync(admin.Id);

        var response = await SendAsync(HttpMethod.Put, $"/api/users/{admin.Id}/roles", new { roleIds = Array.Empty<Guid>() }, token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(AclGuard.LastAdministrator, (await JsonAsync(response)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task Administrator_CanGiveUpTheRole_WhenAnotherActiveAdministratorRemains()
    {
        var (admin, token) = await AdminWithTokenAsync();
        var other = await CreateAdminAsync();
        await DeactivateAllExceptAsync(admin.Id, other.Id);

        var response = await SendAsync(HttpMethod.Put, $"/api/users/{admin.Id}/roles", new { roleIds = Array.Empty<Guid>() }, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task LastAdministrator_CannotBeDeactivatedByAnotherUser()
    {
        var (admin, _) = await AdminWithTokenAsync();
        var (temporary, token) = await TemporaryAdminWithTokenAsync(); // darf alles, ist aber kein dauerhafter Administrator
        await DeactivateAllExceptAsync(admin.Id, temporary.Id);

        var update = await PatchAsync($"/api/users/{admin.Id}", new { active = false }, token);
        var delete = await DeleteAsync($"/api/users/{admin.Id}", token);

        Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, delete.StatusCode);
        Assert.True(await QueryDbAsync(db => db.Users.Where(u => u.Id == admin.Id).Select(u => u.Active).SingleAsync()));
    }
    [Fact]
    public async Task AdministratorCanBeDeactivated_WhenAnotherOneRemains()
    {
        var (first, _) = await AdminWithTokenAsync();
        var (second, token) = await AdminWithTokenAsync();
        await DeactivateAllExceptAsync(first.Id, second.Id);

        Assert.Equal(HttpStatusCode.OK, (await PatchAsync($"/api/users/{first.Id}", new { active = false }, token)).StatusCode);
    }
    [Fact]
    public async Task LastAdministrator_CannotBeMadeToLoseAllPrivilegesThroughItsRole()
    {
        var admin = await CreateAdminAsync(withAdminRole: false);
        var superRole = await CreateRoleAsync(null, Privileges.Everything);
        await AssignAsync(admin.Id, superRole);
        await DeactivateAllExceptAsync(admin.Id);
        var token = await LoginAsync(admin.Username);

        var response = await PatchAsync($"/api/roles/{superRole}", new { privileges = new[] { "user:read" } }, token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(AclGuard.LastAdministrator, (await JsonAsync(response)).GetProperty("message").GetString());
    }
}
