using Rollen_Rechte.Authentication;
using Rollen_Rechte.Common.Api;
using Rollen_Rechte.Contracts;
using Rollen_Rechte.Data;
using Rollen_Rechte.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Rollen_Rechte.Controllers;

/// <summary>
/// Rollenverwaltung (<c>acl_role</c>). Wer Rollen verwalten darf, darf nur Rechte vergeben, die er selbst hat, und keine Rolle
/// bearbeiten, die mehr kann als er (<see cref="AclGuard"/>). Die Rolle "Administrator" ist eingebaut und unveränderlich.
/// </summary>
[Route("api/roles")]
public class RolesController(AccessDbContext db, AclGuard guard) : ApiControllerBase
{
    private const string NameTaken = "Rollenname wird bereits verwendet.";
    private const string RoleInUse = "Die Rolle ist noch Benutzern zugewiesen. Erst die Zuweisungen entfernen.";

    // Die Rollenliste ist klein und wird in der UI komplett gebraucht (Matrix, Auswahl): bewusst ohne Paging.
    [HttpGet, HasPrivilege<AclRole>(CrudAction.Read)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var roles = await db.AclRoles.AsNoTracking().OrderBy(r => r.Name).ToListAsync(ct);
        var counts = await UserCountsAsync(ct);
        return Success(roles.Select(r => RoleResponse.From(r, counts.GetValueOrDefault(r.Id))).ToList());
    }

    [HttpGet("{id:guid}"), HasPrivilege<AclRole>(CrudAction.Read)]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct)
    {
        var role = await db.AclRoles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw ApiException.NotFound();
        return Success(RoleResponse.From(role, (await UserCountsAsync(ct)).GetValueOrDefault(id)));
    }

    [HttpPost, HasPrivilege<AclRole>(CrudAction.Create)]
    public async Task<IActionResult> Create(CreateRoleRequest request, CancellationToken ct)
    {
        var name = request.Name!.Trim();
        var privileges = Normalize(request.Privileges!);
        if (name.Length == 0) throw ApiException.Validation("name", "Dieser Wert darf nicht leer sein.");
        if (AclGuard.IsBuiltIn(name)) throw ApiException.Conflict(NameTaken);

        await guard.EnsureKnownAsync(privileges, ct);
        await guard.EnsureCoversAsync(User.GetUserId(), privileges, ct);
        if (await db.AclRoles.AnyAsync(r => r.Name == name, ct)) throw ApiException.Conflict(NameTaken);

        var role = new AclRole { Name = name, Description = NormalizeDescription(request.Description), Privileges = privileges };
        db.AclRoles.Add(role);
        await SaveAsync(ct);
        return CreatedData(RoleResponse.From(role, 0));
    }

    [HttpPatch("{id:guid}"), HasPrivilege<AclRole>(CrudAction.Update)]
    public async Task<IActionResult> Update(Guid id, UpdateRoleRequest request, CancellationToken ct)
    {
        var role = await db.AclRoles.FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw ApiException.NotFound();
        if (AclGuard.IsBuiltIn(role.Name)) throw ApiException.BadRequest(AclGuard.BuiltInRole);

        var actor = User.GetUserId();
        // Eine Rolle, die mehr kann als der Handelnde, ist für ihn tabu (auch zum Umbenennen).
        await guard.EnsureCoversAsync(actor, role.Privileges, ScopeSet.Everything, ct);

        if (request.Name is not null)
        {
            var name = request.Name.Trim();
            if (name.Length == 0) throw ApiException.Validation("name", "Dieser Wert darf nicht leer sein.");
            if (AclGuard.IsBuiltIn(name) || await db.AclRoles.AnyAsync(r => r.Name == name && r.Id != id, ct))
                throw ApiException.Conflict(NameTaken);
            role.Name = name;
        }

        if (request.Description is not null) role.Description = NormalizeDescription(request.Description);

        if (request.Privileges is not null)
        {
            var privileges = Normalize(request.Privileges);
            await guard.EnsureKnownAsync(privileges, ct);
            await guard.EnsureCoversAsync(actor, privileges, ScopeSet.Everything, ct);
            if (role.Privileges.Contains(Privileges.Everything) && !privileges.Contains(Privileges.Everything))
                await guard.EnsureAdministratorsRemainWithoutRoleAsync(id, ct);
            role.Privileges = privileges;
        }

        await SaveAsync(ct);
        return Success(RoleResponse.From(role, (await UserCountsAsync(ct)).GetValueOrDefault(id)));
    }

    // Hartes Löschen: Rollen haben keinen Archiv-Status. Nur ohne Zuweisungen, damit niemand unbemerkt Rechte verliert.
    [HttpDelete("{id:guid}"), HasPrivilege<AclRole>(CrudAction.Delete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var role = await db.AclRoles.FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw ApiException.NotFound();
        if (AclGuard.IsBuiltIn(role.Name)) throw ApiException.BadRequest(AclGuard.BuiltInRole);

        await guard.EnsureCoversAsync(User.GetUserId(), role.Privileges, ScopeSet.Everything, ct);
        if (await db.AclUserRoles.AnyAsync(x => x.AclRoleId == id, ct)) throw ApiException.Conflict(RoleInUse);

        db.AclRoles.Remove(role);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<Dictionary<Guid, int>> UserCountsAsync(CancellationToken ct) =>
        await db.AclUserRoles.AsNoTracking().GroupBy(x => x.AclRoleId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);

    private static List<string> Normalize(IEnumerable<string> privileges) =>
        privileges.Select(p => p?.Trim() ?? "").Where(p => p.Length > 0).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

    private static string? NormalizeDescription(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            throw ApiException.Conflict(NameTaken);
        }
    }
}
