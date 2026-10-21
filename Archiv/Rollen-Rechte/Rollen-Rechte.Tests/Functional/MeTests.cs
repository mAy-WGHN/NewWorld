using System.Net;
using Rollen_Rechte.Tests.Infrastructure;

namespace Rollen_Rechte.Tests.Functional;

public class MeTests(ApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task ChangePassword_RequiresAuthentication()
    {
        var response = await PatchAsync("/api/me/password", new { currentPassword = DefaultPassword, newPassword = "neues-passwort-1" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_WithWrongCurrentPassword_Returns400_Not401()
    {
        // 401 würde das Frontend ausloggen; ein falsches aktuelles Passwort muss deshalb 400 sein.
        var (_, token) = await AdminWithTokenAsync();

        var response = await PatchAsync("/api/me/password", new { currentPassword = "falsch", newPassword = "neues-passwort-1" }, token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Aktuelles Passwort ist falsch.", (await JsonAsync(response)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task ChangePassword_WithTooShortNewPassword_Returns422()
    {
        var (_, token) = await AdminWithTokenAsync();

        var response = await PatchAsync("/api/me/password", new { currentPassword = DefaultPassword, newPassword = "kurz" }, token);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_AfterwardsOnlyTheNewPasswordWorks()
    {
        var (user, token) = await AdminWithTokenAsync();

        var response = await PatchAsync("/api/me/password", new { currentPassword = DefaultPassword, newPassword = "neues-passwort-1" }, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await JsonAsync(response);
        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await PostAsync("/api/auth/login", new { username = user.Username, password = DefaultPassword })).StatusCode);
        Assert.False(string.IsNullOrEmpty(await LoginAsync(user.Username, "neues-passwort-1")));
    }
}
