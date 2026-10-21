using Rollen_Rechte.Data;
using Rollen_Rechte.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Rollen_Rechte.Authentication;

/// <summary>Die eingebaute Rolle "Administrator" (Recht <c>*</c>): Sie wird beim Start angelegt und von create-user/Seed vergeben.</summary>
public static class AclSeeder
{
    public const string AdministratorRoleName = "Administrator";

    /// <summary>Liefert die Rolle "Administrator" und legt sie an, falls sie fehlt. Die Rechte einer bestehenden Rolle bleiben unangetastet.</summary>
    public static async Task<AclRole> EnsureAdministratorRoleAsync(AccessDbContext db, CancellationToken ct = default)
    {
        var role = await db.AclRoles.FirstOrDefaultAsync(r => r.Name == AdministratorRoleName, ct);
        if (role is not null) return role;

        role = new AclRole
        {
            Name = AdministratorRoleName,
            Description = "Eingebaute Rolle mit allen Rechten",
            Privileges = [Privileges.Everything]
        };
        db.AclRoles.Add(role);
        await db.SaveChangesAsync(ct);
        return role;
    }

    /// <summary>Weist dem User die Rolle zu, falls er sie noch nicht hat (idempotent).</summary>
    public static async Task AssignAsync(AccessDbContext db, Guid userId, Guid roleId, CancellationToken ct = default)
    {
        if (await db.AclUserRoles.AnyAsync(x => x.UserId == userId && x.AclRoleId == roleId, ct)) return;
        db.AclUserRoles.Add(new AclUserRole { UserId = userId, AclRoleId = roleId });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Beim Start; scheitert die DB (z. B. Tabellen fehlen noch), läuft die API trotzdem an und meldet es im Log.</summary>
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        try
        {
            await EnsureAdministratorRoleAsync(scope.ServiceProvider.GetRequiredService<AccessDbContext>());
        }
        catch (Exception ex)
        {
            scope.ServiceProvider.GetRequiredService<ILogger<AuthOptions>>()
                .LogWarning(ex, "Die Rolle \"{Role}\" konnte nicht angelegt werden (Migrationen angewendet?).",
                    AdministratorRoleName);
        }
    }
}
