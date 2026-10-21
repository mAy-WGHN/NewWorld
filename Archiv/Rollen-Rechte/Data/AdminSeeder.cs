using Rollen_Rechte.Authentication;
using Rollen_Rechte.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Rollen_Rechte.Data;

public static class AdminSeeder
{
    /// <summary>
    /// Legt beim Start einen Admin an, wenn Seed__AdminUsername und Seed__AdminPassword gesetzt sind und der
    /// User noch nicht existiert. Ohne diesen Weg gäbe es keinen ersten Login (alle Endpunkte zur User-Anlage
    /// sind selbst geschützt). Bestehende User werden nie verändert.
    /// </summary>
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var username = configuration["Seed:AdminUsername"];
        var password = configuration["Seed:AdminPassword"];
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password)) return;

        var db = scope.ServiceProvider.GetRequiredService<AccessDbContext>();
        if (await db.Users.AnyAsync(u => u.Username == username)) return;

        var passwords = scope.ServiceProvider.GetRequiredService<PasswordService>();
        var admin = new User { Username = username.Trim(), Password = passwords.Hash(password) };
        db.Users.Add(admin);
        await db.SaveChangesAsync();
        await AclSeeder.AssignAsync(db, admin.Id, (await AclSeeder.EnsureAdministratorRoleAsync(db)).Id);
    }
}
