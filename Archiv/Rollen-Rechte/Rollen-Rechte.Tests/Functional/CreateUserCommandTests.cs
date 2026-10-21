using System.Text.RegularExpressions;
using Rollen_Rechte.Commands;
using Rollen_Rechte.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Rollen_Rechte.Tests.Functional;

public class CreateUserCommandTests(ApiFactory factory) : ApiTestBase(factory)
{
    private async Task<(int ExitCode, string Output)> RunAsync(params string[] args)
    {
        var output = new StringWriter();
        var exitCode = await CreateUserCommand.RunAsync(Factory.Services, args, output);
        return (exitCode, output.ToString());
    }

    [Fact]
    public async Task CreatesAnActiveUser_AndShowsTheGeneratedPasswordExactlyOnce()
    {
        var username = Unique("cli");

        var (exitCode, output) = await RunAsync(username);

        Assert.Equal(0, exitCode);
        Assert.Contains($"Benutzer \"{username}\" wurde angelegt.", output);
        var match = Regex.Match(output, @"Passwort \(wird nur jetzt angezeigt\): (\S+)");
        Assert.True(match.Success);
        Assert.Equal(20, match.Groups[1].Value.Length);

        var user = await QueryDbAsync(db => db.Users.AsNoTracking().SingleAsync(u => u.Username == username));
        Assert.True(user.Active);
    }

    [Fact]
    public async Task TheDisplayedPasswordWorksForLogin_AndTheDatabaseStoresOnlyTheHash()
    {
        var username = Unique("cli");
        var (_, output) = await RunAsync(username);
        var password = Regex.Match(output, @"Passwort \(wird nur jetzt angezeigt\): (\S+)").Groups[1].Value;

        var token = await LoginAsync(username, password);

        Assert.False(string.IsNullOrEmpty(token));
        var stored = await QueryDbAsync(db => db.Users.AsNoTracking().SingleAsync(u => u.Username == username));
        Assert.NotEqual(password, stored.Password);
        Assert.DoesNotContain(password, stored.Password);
    }

    [Fact]
    public async Task ExistingUsername_IsRejectedWithAFriendlyMessage_AndExitCode1()
    {
        var existing = await CreateAdminAsync();

        var (exitCode, output) = await RunAsync(existing.Username);

        Assert.Equal(1, exitCode);
        Assert.Contains("existiert bereits", output);
        Assert.DoesNotContain("Passwort", output);
    }

    [Fact]
    public async Task CreatedUser_GetsTheAdministratorRole_AndMayCallProtectedRoutes()
    {
        var username = Unique("cli");
        var (_, output) = await RunAsync(username);
        var password = Regex.Match(output, @"Passwort \(wird nur jetzt angezeigt\): (\S+)").Groups[1].Value;

        var token = await LoginAsync(username, password);

        Assert.Equal(System.Net.HttpStatusCode.OK, (await GetAsync("/api/users", token)).StatusCode);
        var privileges = await QueryDbAsync(db => db.AclUserRoles
            .Where(x => x.User.Username == username && x.AclRole.Name == "Administrator")
            .Select(x => x.AclRole.Privileges).SingleAsync());
        Assert.Equal(["*"], privileges);
    }

    [Fact]
    public async Task AssignRole_GivesAnExistingUserTheRole_AndIsIdempotent()
    {
        var user = await CreateAdminAsync(withAdminRole: false);
        var roleName = Unique("Rolle");
        await Factory.WithDbAsync(async (db, _) =>
        {
            db.AclRoles.Add(new Rollen_Rechte.Domain.Entities.AclRole { Name = roleName, Privileges = ["user:read"] });
            await db.SaveChangesAsync();
        });

        async Task<(int, string)> AssignAsync(params string[] args)
        {
            var output = new StringWriter();
            return (await AssignRoleCommand.RunAsync(Factory.Services, args, output), output.ToString());
        }

        Assert.Equal(0, (await AssignAsync(user.Username, roleName)).Item1);
        Assert.Equal(0, (await AssignAsync(user.Username, roleName)).Item1);
        Assert.Equal(1, await QueryDbAsync(db => db.AclUserRoles.CountAsync(x => x.UserId == user.Id)));

        Assert.Equal(0, (await AssignAsync(user.Username)).Item1); // Standard: Administrator
        Assert.Equal(2, await QueryDbAsync(db => db.AclUserRoles.CountAsync(x => x.UserId == user.Id)));

        Assert.Equal(1, (await AssignAsync("gibt-es-nicht", roleName)).Item1);
        Assert.Equal(1, (await AssignAsync(user.Username, "gibt-es-nicht")).Item1);
        Assert.Equal(2, (await AssignAsync()).Item1);
    }

    [Fact]
    public async Task MissingUsername_PrintsUsage_AndExitCode2()
    {
        var (exitCode, output) = await RunAsync();

        Assert.Equal(2, exitCode);
        Assert.Contains("create-user <username>", output);
    }
}
