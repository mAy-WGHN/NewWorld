using System.Net;
using System.Security.Claims;
using Rollen_Rechte.Authentication;
using Rollen_Rechte.Domain.Entities;
using Rollen_Rechte.Tests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Rollen_Rechte.Tests.Functional;

public class CustomPrivilegeTests(ApiFactory factory) : ApiTestBase(factory)
{
    /// <summary>Eindeutiger Schlüssel pro Test (die Tabelle ist von anderen Tests gefüllt).</summary>
    private static string NewKey(string area = "user") => $"{area}:t{Guid.NewGuid():N}"[..(area.Length + 14)];

    private async Task<Guid> CreateAsync(string token, string key, string label = "Eigenes Recht")
    {
        var response = await PostAsync("/api/custom-privileges", new { key, label }, token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await DataAsync(response)).GetProperty("id").GetGuid();
    }

    private Task<Guid> CreateRoleWithAsync(params string[] privileges) =>
        Factory.WithDbAsync(async (db, _) =>
        {
            var role = new AclRole { Name = Unique("Rolle"), Privileges = [.. privileges] };
            db.AclRoles.Add(role);
            await db.SaveChangesAsync();
            return role.Id;
        });

    private async Task<bool> PolicyAllowsAsync(Guid userId, string key)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId.ToString())], "test"));
        return (await authorization.AuthorizeAsync(principal, null, AuthPolicies.CustomPrefix + key)).Succeeded;
    }

    // --- Anlegen / Ändern ----------------------------------------------------------------------------------

    [Fact]
    public async Task Create_StoresTheRight_AndShowsItInTheCatalogInsideItsArea()
    {
        var (_, token) = await AdminWithTokenAsync();
        var key = NewKey();

        var response = await PostAsync("/api/custom-privileges", new { key, label = " Meldungen exportieren ", description = " Als CSV " }, token);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await DataAsync(response);
        Assert.Equal(key, created.GetProperty("key").GetString());
        Assert.Equal("Meldungen exportieren", created.GetProperty("label").GetString());
        Assert.Equal("Als CSV", created.GetProperty("description").GetString());
        Assert.Equal(0, created.GetProperty("roleCount").GetInt32());

        var catalog = (await DataAsync(await GetAsync("/api/privileges", token))).EnumerateArray().ToList();
        var area = catalog.Single(a => a.GetProperty("key").GetString() == "user");
        var action = area.GetProperty("actions").EnumerateArray().Single(a => a.GetProperty("privilege").GetString() == key);
        Assert.True(action.GetProperty("custom").GetBoolean());
        var generated = area.GetProperty("actions").EnumerateArray().Single(a => a.GetProperty("privilege").GetString() == "user:read");
        Assert.False(generated.GetProperty("custom").GetBoolean());
    }

    [Fact]
    public async Task Create_WithANewArea_AddsAnAreaToTheCatalog()
    {
        var (_, token) = await AdminWithTokenAsync();
        var key = NewKey("lager");
        await CreateAsync(token, key);

        var catalog = (await DataAsync(await GetAsync("/api/privileges", token))).EnumerateArray().ToList();

        var area = catalog.Single(a => a.GetProperty("key").GetString() == "lager");
        Assert.Contains(area.GetProperty("actions").EnumerateArray(), a => a.GetProperty("privilege").GetString() == key);
    }

    [Theory]
    [InlineData("Report:Export")]
    [InlineData("report")]
    [InlineData("report:")]
    [InlineData(":export")]
    [InlineData("report:export:x")]
    [InlineData("user:*")]
    [InlineData("*")]
    [InlineData("1report:export")]
    [InlineData("report export")]
    public async Task Create_WithAMalformedKey_Returns422(string key)
    {
        var (_, token) = await AdminWithTokenAsync();

        var response = await PostAsync("/api/custom-privileges", new { key, label = "x" }, token);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("key", (await JsonAsync(response)).GetProperty("errors")[0].GetProperty("field").GetString());
    }

    [Fact]
    public async Task Create_RejectsDuplicatesAndKeysOfGeneratedRights_AndMissingFields()
    {
        var (_, token) = await AdminWithTokenAsync();
        var key = NewKey();
        await CreateAsync(token, key);

        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync("/api/custom-privileges", new { key, label = "x" }, token)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync("/api/custom-privileges", new { key = "user:read", label = "x" }, token)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await PostAsync("/api/custom-privileges", new { key = NewKey() }, token)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await PostAsync("/api/custom-privileges", new { key = NewKey(), label = "  " }, token)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await PostAsync("/api/custom-privileges", new { label = "x" }, token)).StatusCode);
    }

    [Fact]
    public async Task Update_ChangesLabelAndDescriptionOnly_TheKeyIsImmutable()
    {
        var (_, token) = await AdminWithTokenAsync();
        var key = NewKey();
        var id = await CreateAsync(token, key);

        var response = await PatchAsync($"/api/custom-privileges/{id}", new { key = "report:anders", label = "Neu", description = "Text" }, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await DataAsync(response);
        Assert.Equal(key, data.GetProperty("key").GetString());
        Assert.Equal("Neu", data.GetProperty("label").GetString());
        Assert.Equal("Text", data.GetProperty("description").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await PatchAsync($"/api/custom-privileges/{id}", new { label = " " }, token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PatchAsync($"/api/custom-privileges/{Guid.NewGuid()}", new { label = "x" }, token)).StatusCode);
    }

    [Fact]
    public async Task List_ShowsHowManyRolesUseEachRight()
    {
        var (_, token) = await AdminWithTokenAsync();
        var key = NewKey();
        await CreateAsync(token, key);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync("/api/roles", new { name = Unique("Rolle"), privileges = new[] { key } }, token)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync("/api/roles", new { name = Unique("Rolle"), privileges = new[] { key, "user:read" } }, token)).StatusCode);

        var row = (await DataAsync(await GetAsync("/api/custom-privileges", token))).EnumerateArray().Single(c => c.GetProperty("key").GetString() == key);

        Assert.Equal(2, row.GetProperty("roleCount").GetInt32());
    }

    // --- In Rollen und für die UI ----------------------------------------------------------------------------

    [Fact]
    public async Task Role_CanOnlyContainACustomRightThatExists()
    {
        var (_, token) = await AdminWithTokenAsync();
        var key = NewKey();

        var before = await PostAsync("/api/roles", new { name = Unique("Rolle"), privileges = new[] { key } }, token);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, before.StatusCode);

        await CreateAsync(token, key);

        var after = await PostAsync("/api/roles", new { name = Unique("Rolle"), privileges = new[] { key } }, token);
        Assert.Equal(HttpStatusCode.Created, after.StatusCode);
    }

    [Fact]
    public async Task MyPrivileges_IncludesCustomRights_ThroughTheRoleAndThroughWildcards()
    {
        var (_, admin) = await AdminWithTokenAsync();
        var key = NewKey();
        await CreateAsync(admin, key);
        var (_, direct) = await UserWithPrivilegesAsync(key);
        var (_, wildcard) = await UserWithPrivilegesAsync("user:*");
        var (_, other) = await UserWithPrivilegesAsync("role:read");

        async Task<List<string>> MineAsync(string token) =>
            (await DataAsync(await GetAsync("/api/me/privileges", token))).GetProperty("privileges").EnumerateArray().Select(p => p.GetString()!).ToList();

        Assert.Equal([key], await MineAsync(direct));
        Assert.Contains(key, await MineAsync(wildcard));
        Assert.Contains(key, await MineAsync(admin));
        Assert.DoesNotContain(key, await MineAsync(other));
    }

    [Fact]
    public async Task Role_WithAnUnknownCustomKey_GrantsNothingInTheUi()
    {
        // Ein Schlüssel in einer Rolle ohne Eintrag in custom_privilege (z. B. direkt in die DB geschrieben) wird nicht aufgelöst.
        var key = NewKey();
        var (user, token) = await UserWithPrivilegesAsync(key);

        var mine = (await DataAsync(await GetAsync("/api/me/privileges", token))).GetProperty("privileges").EnumerateArray().ToList();

        Assert.Empty(mine);
        Assert.NotNull(user);
    }

    // --- Erzwingen per [HasCustomPrivilege] ------------------------------------------------------------------------

    [Fact]
    public async Task CustomPolicy_RequiresTheRight_ToExist_AndToBeGranted()
    {
        var (_, admin) = await AdminWithTokenAsync();
        var key = NewKey();
        var id = await CreateAsync(admin, key);
        var withRight = await CreateAdminAsync(withAdminRole: false);
        var without = await CreateAdminAsync(withAdminRole: false);
        var superAdmin = await CreateAdminAsync();
        var roleId = await CreateRoleWithAsync(key);
        await Factory.WithDbAsync(async (db, _) =>
        {
            db.AclUserRoles.Add(new AclUserRole { UserId = withRight.Id, AclRoleId = roleId });
            await db.SaveChangesAsync();
        });

        Assert.True(await PolicyAllowsAsync(withRight.Id, key));
        Assert.False(await PolicyAllowsAsync(without.Id, key));
        Assert.True(await PolicyAllowsAsync(superAdmin.Id, key)); // "*" deckt es ab

        // Gelöscht: Es existiert nicht mehr, niemand darf mehr — auch nicht über die alte Rolle oder "*".
        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync($"/api/custom-privileges/{id}", admin)).StatusCode);
        Assert.False(await PolicyAllowsAsync(withRight.Id, key));
        Assert.False(await PolicyAllowsAsync(superAdmin.Id, key));
    }

    [Fact]
    public async Task CustomPolicy_ForAKeyThatWasNeverCreated_DeniesEveryone()
    {
        var admin = await CreateAdminAsync();

        Assert.False(await PolicyAllowsAsync(admin.Id, NewKey()));
    }

    // --- Löschen -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Delete_RemovesTheRightFromAllRoles_AndFromTheCatalog()
    {
        var (_, token) = await AdminWithTokenAsync();
        var key = NewKey();
        var id = await CreateAsync(token, key);
        var roleId = (await DataAsync(await PostAsync("/api/roles", new { name = Unique("Rolle"), privileges = new[] { key, "user:read" } }, token)))
            .GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync($"/api/custom-privileges/{id}", token)).StatusCode);

        var privileges = await QueryDbAsync(db => db.AclRoles.Where(r => r.Id == roleId).Select(r => r.Privileges).SingleAsync());
        Assert.Equal(["user:read"], privileges);
        Assert.False(await QueryDbAsync(db => db.CustomPrivileges.AnyAsync(c => c.Id == id)));
        var catalog = await DataAsync(await GetAsync("/api/privileges", token));
        Assert.DoesNotContain(catalog.EnumerateArray().SelectMany(a => a.GetProperty("actions").EnumerateArray()),
            a => a.GetProperty("privilege").GetString() == key);
        Assert.Equal(HttpStatusCode.NotFound, (await DeleteAsync($"/api/custom-privileges/{id}", token)).StatusCode);
    }

    [Fact]
    public async Task Delete_NeedsTheRightItself_SoNobodyStripsWhatHeDoesNotHave()
    {
        var (_, admin) = await AdminWithTokenAsync();
        var key = NewKey();
        var id = await CreateAsync(admin, key);
        var (_, manager) = await UserWithPrivilegesAsync("custom_privilege:delete");

        Assert.Equal(HttpStatusCode.Forbidden, (await DeleteAsync($"/api/custom-privileges/{id}", manager)).StatusCode);
        Assert.True(await QueryDbAsync(db => db.CustomPrivileges.AnyAsync(c => c.Id == id)));
    }

    // --- Verwaltung ist selbst ein generiertes Recht ----------------------------------------------------------

    [Fact]
    public async Task Management_IsGatedByTheGeneratedCustomPrivilegeRights()
    {
        var (_, reader) = await UserWithPrivilegesAsync("custom_privilege:read");
        var (_, creator) = await UserWithPrivilegesAsync("custom_privilege:create");
        var (_, nobody) = await UserWithPrivilegesAsync();

        Assert.Equal(HttpStatusCode.OK, (await GetAsync("/api/custom-privileges", reader)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync("/api/custom-privileges", new { key = NewKey(), label = "x" }, reader)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync("/api/custom-privileges", new { key = NewKey(), label = "x" }, creator)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync("/api/custom-privileges", nobody)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync("/api/custom-privileges")).StatusCode);
    }

    [Fact]
    public async Task ARoleManager_CannotGrantACustomRightHeDoesNotHave()
    {
        var (_, admin) = await AdminWithTokenAsync();
        var key = NewKey();
        await CreateAsync(admin, key);
        var (_, manager) = await UserWithPrivilegesAsync("role:read", "role:create");

        var response = await PostAsync("/api/roles", new { name = Unique("Rolle"), privileges = new[] { key } }, manager);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
