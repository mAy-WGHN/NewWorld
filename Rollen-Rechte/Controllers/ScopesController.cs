using Rollen_Rechte.Authentication;
using Rollen_Rechte.Common.Api;
using Rollen_Rechte.Contracts;
using Rollen_Rechte.Data;
using Rollen_Rechte.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Rollen_Rechte.Controllers;

/// <summary>
/// Scopes (<c>scope</c>): benannte Reichweiten wie "Region Nord" aus Filialen und/oder Einzelmeldungen, in der UI pflegbar und an
/// Rollenzuweisungen gehängt (<c>PUT /api/users/{id}/roles</c>). Die Rolle sagt, was jemand darf, der Scope, wo.
/// </summary>
[Route("api/scopes")]
public class ScopesController(AccessDbContext db, ScopeContext scope, AclGuard guard) : ApiControllerBase
{
    private const string NameTaken = "Scope-Name wird bereits verwendet.";
    private const string InUse = "Der Scope ist noch Rollenzuweisungen zugeordnet. Erst die Zuweisungen ändern.";

    // Wer nur eingeschränkt lesen darf (z. B. ein Regionalleiter beim Zuweisen), sieht nur Scopes, die vollständig in seiner Reichweite liegen.
    [HttpGet, HasPrivilege<Scope>(CrudAction.Read)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var scopes = await db.Scopes.AsNoTracking().Include(s => s.Members).OrderBy(s => s.Name).ToListAsync(ct);
        var counts = await AssignmentCountsAsync(ct);
        return Success(scopes.Where(s => scope.Current.Covers(ToSet(s.Members)))
            .Select(s => ScopeResponse.From(s, counts.GetValueOrDefault(s.Id))).ToList());
    }

    [HttpGet("{id:guid}"), HasPrivilege<Scope>(CrudAction.Read)]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct)
    {
        var entity = await FindAsync(id, CrudAction.Read, ct);
        return Success(ScopeResponse.From(entity, (await AssignmentCountsAsync(ct)).GetValueOrDefault(id)));
    }

    [HttpPost, HasPrivilege<Scope>(CrudAction.Create)]
    public async Task<IActionResult> Create(CreateScopeRequest request, CancellationToken ct)
    {
        var name = request.Name!.Trim();
        if (name.Length == 0) throw ApiException.Validation("name", "Dieser Wert darf nicht leer sein.");
        if (await db.Scopes.AnyAsync(s => s.Name == name, ct)) throw ApiException.Conflict(NameTaken);

        var entity = new Scope { Name = name, Description = NormalizeDescription(request.Description) };
        await ApplyMembersAsync(entity, request.Members ?? [], ct);
        await guard.EnsureReachCoversAsync(User.GetUserId(), ToSet(entity.Members), ct);
        db.Scopes.Add(entity);
        await SaveAsync(ct);
        return CreatedData(ScopeResponse.From(entity, 0));
    }

    [HttpPatch("{id:guid}"), HasPrivilege<Scope>(CrudAction.Update)]
    public async Task<IActionResult> Update(Guid id, UpdateScopeRequest request, CancellationToken ct)
    {
        var entity = await FindAsync(id, CrudAction.Update, ct);

        if (request.Name is not null)
        {
            var name = request.Name.Trim();
            if (name.Length == 0) throw ApiException.Validation("name", "Dieser Wert darf nicht leer sein.");
            if (await db.Scopes.AnyAsync(s => s.Name == name && s.Id != id, ct)) throw ApiException.Conflict(NameTaken);
            entity.Name = name;
        }

        if (request.Description is not null) entity.Description = NormalizeDescription(request.Description);
        if (request.Members is not null)
        {
            // Berührt sind alte und neue Mitglieder: Der Scope ändert die Reichweite aller Zuweisungen, die ihn tragen.
            var before = ToSet(entity.Members);
            await ApplyMembersAsync(entity, request.Members, ct);
            await guard.EnsureReachCoversAsync(User.GetUserId(), before.Union(ToSet(entity.Members)), ct);
        }

        await SaveAsync(ct);
        return Success(ScopeResponse.From(entity, (await AssignmentCountsAsync(ct)).GetValueOrDefault(id)));
    }

    [HttpDelete("{id:guid}"), HasPrivilege<Scope>(CrudAction.Delete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var entity = await FindAsync(id, CrudAction.Delete, ct);
        await guard.EnsureReachCoversAsync(User.GetUserId(), ToSet(entity.Members), ct);
        if (await db.AclUserRoleScopes.AnyAsync(s => s.ScopeId == id, ct)) throw ApiException.Conflict(InUse);

        db.Scopes.Remove(entity);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<Scope> FindAsync(Guid id, CrudAction action, CancellationToken ct)
    {
        var entity = await db.Scopes.Include(s => s.Members).FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw ApiException.NotFound();
        if (!scope.Current.Covers(ToSet(entity.Members))) throw AuthMessages.Forbidden(action);
        return entity;
    }

    private async Task ApplyMembersAsync(Scope entity, List<ScopeMemberDto> members, CancellationToken ct)
    {
        var distinct = members.Select(m => (Dimension: m.Dimension?.Trim() ?? "", m.RefId)).Distinct().ToList();

        var unknown = distinct.FirstOrDefault(m => !ScopeDimensions.All.Contains(m.Dimension));
        if (unknown.Dimension is not null)
            throw ApiException.Validation("members", $"Unbekannte Dimension: {unknown.Dimension} (erlaubt: {string.Join(", ", ScopeDimensions.All)}).");

        var locationIds = distinct.Where(m => m.Dimension == ScopeDimensions.Location).Select(m => m.RefId).ToList();
        if (await db.Locations.CountAsync(l => locationIds.Contains(l.Id), ct) != locationIds.Count)
            throw ApiException.Validation("members", "Eine oder mehrere Filialen wurden nicht gefunden.");

        // Einzelmeldungen (Dimension "report") führt das Portal: Dieser Dienst speichert die ID, ohne zu prüfen, ob es die Meldung gibt.
        // Eine unbekannte ID gewährt nichts — die Anwendung findet dazu keine Meldung.
        entity.Members.RemoveAll(m => !distinct.Contains((m.Dimension, m.RefId)));
        foreach (var (dimension, refId) in distinct.Where(d => !entity.Members.Any(m => m.Dimension == d.Dimension && m.RefId == d.RefId)))
            entity.Members.Add(new ScopeMember { Dimension = dimension, RefId = refId });
    }

    private async Task<Dictionary<Guid, int>> AssignmentCountsAsync(CancellationToken ct) =>
        await db.AclUserRoleScopes.AsNoTracking().GroupBy(s => s.ScopeId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);

    private static ScopeSet ToSet(IEnumerable<ScopeMember> members)
    {
        var list = members.ToList();
        return ScopeSet.Of(
            list.Where(m => m.Dimension == ScopeDimensions.Location).Select(m => m.RefId),
            list.Where(m => m.Dimension == ScopeDimensions.Report).Select(m => m.RefId));
    }

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
