using Rollen_Rechte.Authentication;
using Rollen_Rechte.Data;
using Microsoft.EntityFrameworkCore;

namespace Rollen_Rechte.Commands;

/// <summary>
/// "dotnet Rollen-Rechte.dll assign-role &lt;username&gt; [rolle]": weist einem User eine ACL-Rolle zu (Standard: "Administrator").
/// Nötig, weil User ohne Rolle keine Rechte haben und es noch keine Endpunkte zur Rollenverwaltung gibt.
/// </summary>
public static class AssignRoleCommand
{
    public const string Name = "assign-role";

    public static async Task<int> RunAsync(IServiceProvider services, string[] args, TextWriter output)
    {
        var username = args.ElementAtOrDefault(0)?.Trim();
        var roleName = args.ElementAtOrDefault(1)?.Trim();
        if (string.IsNullOrEmpty(roleName)) roleName = AclSeeder.AdministratorRoleName;
        if (string.IsNullOrEmpty(username))
        {
            await output.WriteLineAsync($"Aufruf: {Name} <username> [rolle]");
            return 2;
        }

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AccessDbContext>();

        var userId = await db.Users.Where(u => u.Username == username).Select(u => (Guid?)u.Id).FirstOrDefaultAsync();
        if (userId is null)
        {
            await output.WriteLineAsync($"Benutzer \"{username}\" existiert nicht.");
            return 1;
        }

        var role = roleName == AclSeeder.AdministratorRoleName
            ? await AclSeeder.EnsureAdministratorRoleAsync(db)
            : await db.AclRoles.FirstOrDefaultAsync(r => r.Name == roleName);
        if (role is null)
        {
            await output.WriteLineAsync($"Rolle \"{roleName}\" existiert nicht.");
            return 1;
        }

        await AclSeeder.AssignAsync(db, userId.Value, role.Id);
        await output.WriteLineAsync($"Benutzer \"{username}\" hat die Rolle \"{role.Name}\".");
        return 0;
    }
}
