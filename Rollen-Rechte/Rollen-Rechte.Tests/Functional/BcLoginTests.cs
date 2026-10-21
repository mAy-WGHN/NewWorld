using System.Net;
using Rollen_Rechte.Authentication;
using Rollen_Rechte.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Rollen_Rechte.Tests.Functional;

public class BcLoginTests(ApiFactory factory) : ApiTestBase(factory)
{
    private static string PersonnelNo() => Digits(8);

    private Task<HttpResponseMessage> LoginRawAsync(string username, string password) =>
        PostAsync("/api/auth/login", new { username, password });

    private Task<Rollen_Rechte.Domain.Entities.User?> FindUserAsync(string username) =>
        QueryDbAsync(db => db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == username));

    [Fact]
    public async Task Login_WithBcCredentials_CreatesUserAndReturnsToken()
    {
        var no = PersonnelNo();
        Factory.Bc.AddEmployee(no, "4711");

        var response = await LoginRawAsync(no, "4711");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = await FindUserAsync(no);
        Assert.NotNull(user);
        Assert.True(user.Active);
        // Passwort als Hash (für den Offline-Fallback) UND reversibel verschlüsselt (entschlüsselbar mit dem Schlüssel).
        Assert.StartsWith("$2", user.Password);
        Assert.NotNull(user.BcPinEncrypted);
        Assert.DoesNotContain("4711", user.BcPinEncrypted);
        var protector = Factory.Services.GetRequiredService<PinProtector>();
        Assert.Equal("4711", protector.Unprotect(user.BcPinEncrypted));
    }

    [Fact]
    public async Task Login_WithWrongBcPin_ForUnknownUser_Returns401AndCreatesNothing()
    {
        var no = PersonnelNo();
        Factory.Bc.AddEmployee(no, "4711");

        var response = await LoginRawAsync(no, "0000");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(await FindUserAsync(no));
    }

    [Fact]
    public async Task Login_AfterPinChangeInBc_RejectsTheOldPinEvenThoughItIsStoredLocally()
    {
        var no = PersonnelNo();
        Factory.Bc.AddEmployee(no, "1111");
        Assert.Equal(HttpStatusCode.OK, (await LoginRawAsync(no, "1111")).StatusCode);

        Factory.Bc.SetPin(no, "2222");

        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginRawAsync(no, "1111")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginRawAsync(no, "2222")).StatusCode);

        // Der neue Stand ist lokal hinterlegt: Hash und verschlüsseltes PIN.
        var user = await FindUserAsync(no);
        Assert.Equal("2222", Factory.Services.GetRequiredService<PinProtector>().Unprotect(user!.BcPinEncrypted!));
        Assert.True(Factory.Services.GetRequiredService<PasswordService>().Verify("2222", user.Password));
    }

    [Fact]
    public async Task Login_WhenBcIsUnavailable_FallsBackToTheStoredPin()
    {
        var no = PersonnelNo();
        Factory.Bc.AddEmployee(no, "4711");
        Assert.Equal(HttpStatusCode.OK, (await LoginRawAsync(no, "4711")).StatusCode);

        Factory.Bc.Unavailable = true;

        Assert.Equal(HttpStatusCode.OK, (await LoginRawAsync(no, "4711")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginRawAsync(no, "9999")).StatusCode);
    }

    [Fact]
    public async Task Login_WhenBcIsUnavailable_DoesNotCreateUnknownUsers()
    {
        var no = PersonnelNo();
        Factory.Bc.AddEmployee(no, "4711");
        Factory.Bc.Unavailable = true;

        var response = await LoginRawAsync(no, "4711");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(await FindUserAsync(no));
    }

    [Fact]
    public async Task Login_WithDeactivatedBcUser_Returns401WithoutAskingBc()
    {
        var no = PersonnelNo();
        Factory.Bc.AddEmployee(no, "4711");
        Assert.Equal(HttpStatusCode.OK, (await LoginRawAsync(no, "4711")).StatusCode);
        await Factory.WithDbAsync(async (db, _) =>
            await db.Users.Where(u => u.Username == no).ExecuteUpdateAsync(s => s.SetProperty(u => u.Active, false)));
        var callsBefore = Factory.Bc.StatusCalls.Count;

        var response = await LoginRawAsync(no, "4711");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(callsBefore, Factory.Bc.StatusCalls.Count);
    }

    [Fact]
    public async Task Login_WithNonNumericUsername_NeverAsksBc()
    {
        var user = await CreateAdminAsync();

        await LoginAsync(user.Username);

        Assert.DoesNotContain(user.Username, Factory.Bc.StatusCalls);
    }

    [Fact]
    public async Task Login_WithNumericLocalUserUnknownToBc_UsesTheLocalPassword()
    {
        var no = PersonnelNo();
        await CreateAdminAsync(username: no);

        Assert.Equal(HttpStatusCode.OK, (await LoginRawAsync(no, DefaultPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginRawAsync(no, "falsch")).StatusCode);
    }

    [Fact]
    public async Task Login_BcToken_IsAccepted_ButAutoCreatedUserHasNoPrivileges()
    {
        var no = PersonnelNo();
        Factory.Bc.AddEmployee(no, "4711");
        var response = await LoginRawAsync(no, "4711");
        var token = (await JsonAsync(response)).GetProperty("token").GetString();

        Assert.Equal(HttpStatusCode.OK, (await GetAsync("/api/me/privileges", token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync("/api/users", token)).StatusCode);
        Assert.False(await QueryDbAsync(db => db.AclUserRoles.AnyAsync(x => x.User.Username == no)));
    }
}
