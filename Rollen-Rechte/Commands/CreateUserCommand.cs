using System.Security.Cryptography;
using Rollen_Rechte.Authentication;
using Rollen_Rechte.Data;
using Rollen_Rechte.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Rollen_Rechte.Commands;

/// <summary>
/// "dotnet Rollen-Rechte.dll create-user &lt;username&gt;": legt einen Admin mit zufälligem 20-stelligem Passwort an und
/// zeigt es genau einmal an (nur der Hash wird gespeichert). Das Passwort landet nie im Log.
/// Der einzige Weg zum ersten Admin: es gibt keine Registrierung, und POST /api/users verlangt bereits einen Admin.
/// </summary>
public static class CreateUserCommand
{
    public const string Name = "create-user";
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";

    public static async Task<int> RunAsync(IServiceProvider services, string[] args, TextWriter output)
    {
        var username = args.FirstOrDefault()?.Trim();
        if (string.IsNullOrEmpty(username))
        {
            await output.WriteLineAsync($"Aufruf: {Name} <username>");
            return 2;
        }

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AccessDbContext>();
        var passwords = scope.ServiceProvider.GetRequiredService<PasswordService>();

        if (await db.Users.AnyAsync(u => u.Username == username))
        {
            await output.WriteLineAsync($"Benutzer \"{username}\" existiert bereits.");
            return 1;
        }

        var password = RandomNumberGenerator.GetString(Alphabet, 20);
        var user = new User { Username = username, Password = passwords.Hash(password) };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        await AclSeeder.AssignAsync(db, user.Id, (await AclSeeder.EnsureAdministratorRoleAsync(db)).Id);

        await output.WriteLineAsync($"Benutzer \"{username}\" wurde angelegt.");
        await output.WriteLineAsync($"Passwort (wird nur jetzt angezeigt): {password}");
        return 0;
    }
}
