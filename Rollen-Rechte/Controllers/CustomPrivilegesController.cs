using System.Text.RegularExpressions;
using Rollen_Rechte.Authentication;
using Rollen_Rechte.Common.Api;
using Rollen_Rechte.Contracts;
using Rollen_Rechte.Data;
using Rollen_Rechte.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Rollen_Rechte.Controllers;

/// <summary>
/// Zur Laufzeit angelegte Rechte (<c>custom_privilege</c>). Die Rechte für diese Verwaltung (<c>custom_privilege:read/create/update/delete</c>)
/// werden wie bei jeder Entity generiert. Ein eigenes Recht gilt, sobald es hier steht: Es lässt sich in Rollen vergeben, die UI sieht es
/// in <c>/api/me/privileges</c>, und Routen mit <c>[HasCustomPrivilege]</c> erzwingen es.
/// </summary>
[Route("api/custom-privileges")]
public partial class CustomPrivilegesController(AccessDbContext db, AclGuard guard, PrivilegeCatalogService catalog) : ApiControllerBase
{
    private const string KeyTaken = "Dieses Recht existiert bereits.";

    [GeneratedRegex("^[a-z][a-z0-9_]*:[a-z][a-z0-9_]*$")]
    private static partial Regex KeyFormat();

    [HttpGet, HasPrivilege<CustomPrivilege>(CrudAction.Read)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var rows = await db.CustomPrivileges.AsNoTracking().OrderBy(c => c.Key).ToListAsync(ct);
        var counts = await RoleCountsAsync(ct);
        return Success(rows.Select(c => CustomPrivilegeResponse.From(c, counts.GetValueOrDefault(c.Key))).ToList());
    }

    [HttpPost, HasPrivilege<CustomPrivilege>(CrudAction.Create)]
    public async Task<IActionResult> Create(CreateCustomPrivilegeRequest request, CancellationToken ct)
    {
        var key = request.Key!.Trim();
        var label = request.Label!.Trim();
        if (!KeyFormat().IsMatch(key))
            throw ApiException.Validation("key", "Format: bereich:aktion in Kleinbuchstaben, Ziffern und Unterstrich (z. B. report:export).");
        if (label.Length == 0) throw ApiException.Validation("label", "Dieser Wert darf nicht leer sein.");
        if ((await catalog.GetAsync(ct)).All.Contains(key) || await db.CustomPrivileges.AnyAsync(c => c.Key == key, ct))
            throw ApiException.Conflict(KeyTaken);

        var privilege = new CustomPrivilege { Key = key, Label = label, Description = NormalizeDescription(request.Description) };
        db.CustomPrivileges.Add(privilege);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            throw ApiException.Conflict(KeyTaken);
        }

        catalog.Invalidate();
        return CreatedData(CustomPrivilegeResponse.From(privilege, 0));
    }

    [HttpPatch("{id:guid}"), HasPrivilege<CustomPrivilege>(CrudAction.Update)]
    public async Task<IActionResult> Update(Guid id, UpdateCustomPrivilegeRequest request, CancellationToken ct)
    {
        var privilege = await db.CustomPrivileges.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw ApiException.NotFound();

        if (request.Label is not null)
        {
            var label = request.Label.Trim();
            if (label.Length == 0) throw ApiException.Validation("label", "Dieser Wert darf nicht leer sein.");
            privilege.Label = label;
        }

        if (request.Description is not null) privilege.Description = NormalizeDescription(request.Description);

        await db.SaveChangesAsync(ct);
        catalog.Invalidate();
        return Success(CustomPrivilegeResponse.From(privilege, (await RoleCountsAsync(ct)).GetValueOrDefault(privilege.Key)));
    }

    // Entzieht das Recht allen Rollen und löscht es dann. Wie überall gilt: Man entzieht nur, was man selbst hat.
    [HttpDelete("{id:guid}"), HasPrivilege<CustomPrivilege>(CrudAction.Delete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var privilege = await db.CustomPrivileges.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw ApiException.NotFound();
        await guard.EnsureCoversAsync(User.GetUserId(), [privilege.Key], ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        foreach (var role in await db.AclRoles.ToListAsync(ct))
        {
            if (role.Privileges.Contains(privilege.Key)) role.Privileges = role.Privileges.Where(p => p != privilege.Key).ToList();
        }

        db.CustomPrivileges.Remove(privilege);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        catalog.Invalidate();
        return NoContent();
    }

    private async Task<Dictionary<string, int>> RoleCountsAsync(CancellationToken ct)
    {
        var roles = await db.AclRoles.AsNoTracking().Select(r => r.Privileges).ToListAsync(ct);
        return roles.SelectMany(p => p).GroupBy(p => p).ToDictionary(g => g.Key, g => g.Count());
    }

    private static string? NormalizeDescription(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();
}
