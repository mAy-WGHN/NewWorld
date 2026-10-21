using System.Net;
using System.Text.Json;
using Rollen_Rechte.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Rollen_Rechte.Tests.Functional;

public class UserTests(ApiFactory factory) : ApiTestBase(factory)
{
    private static string[] Fields(JsonElement response) =>
        response.GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("field").GetString()!).ToArray();

    // --- Zugriff ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Endpoints_RequireAdmin()
    {
        var user = await CreateAdminAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync("/api/users", new { username = "x", password = "langgenug1" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PatchAsync($"/api/users/{user.Id}", new { active = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PatchAsync($"/api/users/{user.Id}/password", new { password = "langgenug1" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await DeleteAsync($"/api/users/{user.Id}")).StatusCode);
    }

    // --- Liste -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task List_ReturnsUsersWithoutPasswordHash_IncludingDeactivatedOnes()
    {
        var (_, token) = await AdminWithTokenAsync();
        var deactivated = await CreateAdminAsync(active: false);

        var response = await GetAsync("/api/users", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("password", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(deactivated.Username, text);
    }

    [Fact]
    public async Task List_WithoutLimitReturnsEverything_WithExplicitLimitItPaginates()
    {
        var (_, token) = await AdminWithTokenAsync();
        await CreateAdminAsync();
        await CreateAdminAsync();

        var all = await DataAsync(await GetAsync("/api/users", token));
        var limited = await DataAsync(await GetAsync("/api/users?limit=1", token));

        Assert.Equal(JsonValueKind.Null, all.GetProperty("meta").GetProperty("limit").ValueKind);
        Assert.True(all.GetProperty("items").GetArrayLength() >= 3);
        Assert.Equal(1, limited.GetProperty("items").GetArrayLength());
        Assert.Equal(1, limited.GetProperty("meta").GetProperty("limit").GetInt32());
    }

    [Fact]
    public async Task List_SortsByUsername_AscendingByDefault()
    {
        var (_, token) = await AdminWithTokenAsync();
        await CreateAdminAsync();

        var data = await DataAsync(await GetAsync("/api/users", token));

        var names = data.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("username").GetString()!).ToList();
        Assert.Equal(names.OrderBy(n => n, StringComparer.Ordinal).ToList(), names);
    }

    // --- Anlegen ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Create_AsAdmin_Returns201_WithoutAnyRole()
    {
        var (_, token) = await AdminWithTokenAsync();

        var response = await PostAsync("/api/users", new { username = Unique("neu"), password = "langgenug1" }, token);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var data = await DataAsync(response);
        Assert.True(data.GetProperty("active").GetBoolean());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("homeLocationId").ValueKind);
        // Ein neuer User hat keine Rolle und damit keine Rechte.
        Assert.False(await QueryDbAsync(db => db.AclUserRoles.AnyAsync(x => x.UserId == data.GetProperty("id").GetGuid())));
    }

    [Fact]
    public async Task Create_StoresOnlyAHash_AndTheNewUserCanLogIn()
    {
        var (_, token) = await AdminWithTokenAsync();
        var username = Unique("login");

        await PostAsync("/api/users", new { username, password = "langgenug1" }, token);

        var stored = await QueryDbAsync(db => db.Users.AsNoTracking().SingleAsync(u => u.Username == username));
        Assert.NotEqual("langgenug1", stored.Password);
        Assert.StartsWith("$2", stored.Password);
        Assert.False(string.IsNullOrEmpty(await LoginAsync(username, "langgenug1")));
    }

    [Fact]
    public async Task Create_WithDuplicateUsername_Returns409()
    {
        var (_, token) = await AdminWithTokenAsync();
        var existing = await CreateAdminAsync();

        var response = await PostAsync("/api/users", new { username = existing.Username, password = "langgenug1" }, token);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Benutzername wird bereits verwendet.", (await JsonAsync(response)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task Create_WithInvalidData_Returns422()
    {
        var (_, token) = await AdminWithTokenAsync();

        var response = await PostAsync("/api/users", new { username = "", password = "kurz" }, token);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var fields = Fields(await JsonAsync(response));
        Assert.Contains("username", fields);
        Assert.Contains("password", fields);
    }

    // --- Passwort zurücksetzen --------------------------------------------------------------------------------

    [Fact]
    public async Task ResetPassword_ReplacesThePassword_WithoutKnowingTheOldOne()
    {
        var (_, token) = await AdminWithTokenAsync();
        var target = await CreateAdminAsync();

        var response = await PatchAsync($"/api/users/{target.Id}/password", new { password = "ganz-neues-passwort" }, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await PostAsync("/api/auth/login", new { username = target.Username, password = DefaultPassword })).StatusCode);
        Assert.False(string.IsNullOrEmpty(await LoginAsync(target.Username, "ganz-neues-passwort")));
    }

    [Fact]
    public async Task ResetPassword_WithTooShortPassword_Returns422()
    {
        var (_, token) = await AdminWithTokenAsync();
        var target = await CreateAdminAsync();

        var response = await PatchAsync($"/api/users/{target.Id}/password", new { password = "kurz" }, token);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // --- Aktivieren / Deaktivieren -------------------------------------------------------------------------------

    [Fact]
    public async Task Update_CanDeactivateAndReactivateAnotherUser()
    {
        var (_, token) = await AdminWithTokenAsync();
        var target = await CreateAdminAsync();

        var off = await DataAsync(await PatchAsync($"/api/users/{target.Id}", new { active = false }, token));
        var on = await DataAsync(await PatchAsync($"/api/users/{target.Id}", new { active = true }, token));

        Assert.False(off.GetProperty("active").GetBoolean());
        Assert.True(on.GetProperty("active").GetBoolean());
    }

    [Fact]
    public async Task Delete_DeactivatesInsteadOfDeleting()
    {
        var (_, token) = await AdminWithTokenAsync();
        var target = await CreateAdminAsync();

        var response = await DeleteAsync($"/api/users/{target.Id}", token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var stored = await QueryDbAsync(db => db.Users.AsNoTracking().SingleAsync(u => u.Id == target.Id));
        Assert.False(stored.Active);
    }

    [Fact]
    public async Task OwnAccount_CannotBeDeactivated_NeitherViaPatchNorViaDelete()
    {
        var (me, token) = await AdminWithTokenAsync();

        var patch = await PatchAsync($"/api/users/{me.Id}", new { active = false }, token);
        var delete = await DeleteAsync($"/api/users/{me.Id}", token);

        Assert.Equal(HttpStatusCode.BadRequest, patch.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, delete.StatusCode);
        Assert.Equal("Der eigene Account kann nicht deaktiviert werden.",
            (await JsonAsync(patch)).GetProperty("message").GetString());
        var stored = await QueryDbAsync(db => db.Users.AsNoTracking().SingleAsync(u => u.Id == me.Id));
        Assert.True(stored.Active);
    }

    [Fact]
    public async Task Update_OnAnAlreadyDeletedUser_Returns404WithFriendlyMessage()
    {
        var (_, token) = await AdminWithTokenAsync();

        var response = await PatchAsync($"/api/users/{Guid.NewGuid()}", new { active = false }, token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("nicht gefunden", (await JsonAsync(response)).GetProperty("message").GetString());
    }

    // --- Benachrichtigungs-Einstellungen --------------------------------------------------------------------------

    [Fact]
    public async Task Update_CannotChangeTheUsername()
    {
        var (_, token) = await AdminWithTokenAsync();
        var target = await CreateAdminAsync();

        var data = await DataAsync(await PatchAsync($"/api/users/{target.Id}", new { username = "anderer-name" }, token));

        Assert.Equal(target.Username, data.GetProperty("username").GetString());
    }
}
