using Rollen_Rechte.Authentication;
using Rollen_Rechte.Common.Api;
using Rollen_Rechte.Contracts;
using Rollen_Rechte.Data;
using Rollen_Rechte.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Rollen_Rechte.Controllers;

[Route("api/users")]
public class UsersController(
    AccessDbContext db, PasswordService passwords, AclGuard guard, ScopeContext scope, TimeProvider clock) : ApiControllerBase
{
    private const string UsernameTaken = "Benutzername wird bereits verwendet.";
    private const string SelfDeactivation = "Der eigene Account kann nicht deaktiviert werden.";

    [HttpGet, HasPrivilege<User>(CrudAction.Read)]
    public async Task<IActionResult> List([FromQuery] UserFilterRequest filter, CancellationToken ct)
    {
        var users = db.Users.InScope(db, scope.Current);
        var desc = filter.IsDescending(false);
        var ordered = (filter.SortBy ?? "username") switch
        {
            "id" => users.OrderByDirection(u => u.Id, desc),
            "createdAt" => users.OrderByDirection(u => u.CreatedAt, desc),
            "updatedAt" => users.OrderByDirection(u => u.UpdatedAt, desc),
            _ => users.OrderByDirection(u => u.Username, desc)
        };

        // Enthält bewusst auch deaktivierte User. Mapping im Speicher, damit kein Hash in die Projektion gerät.
        var page = await ordered.ThenBy(u => u.Id).ToPagedAsync(filter.Offset, filter.Limit, ct);
        return Success(new PagedResult<UserResponse>(page.Items.Select(UserResponse.From).ToList(), page.Meta));
    }

    [HttpPost, HasPrivilege<User>(CrudAction.Create)]
    public async Task<IActionResult> Create(CreateUserRequest request, CancellationToken ct)
    {
        // Ein neuer User gehört noch zu keiner Filiale: Anlegen verlangt uneingeschränkte Reichweite.
        if (!scope.Current.All) throw AuthMessages.Forbidden(CrudAction.Create);

        var username = request.Username!.Trim();
        if (await db.Users.AnyAsync(u => u.Username == username, ct))
            throw ApiException.Conflict(UsernameTaken);

        var user = new User { Username = username, Password = passwords.Hash(request.Password!) };
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            throw ApiException.Conflict(UsernameTaken);
        }

        return CreatedData(UserResponse.From(user));
    }

    [HttpPatch("{id:guid}"), HasPrivilege<User>(CrudAction.Update)]
    public async Task<IActionResult> Update(Guid id, UpdateUserRequest request, CancellationToken ct)
    {
        var user = await FindInScopeAsync(id, CrudAction.Update, ct);

        if (request.Active == false && id == User.GetUserId())
            throw ApiException.BadRequest(SelfDeactivation);
        if (request.Active is not null) await guard.EnsureCoversUserAsync(User.GetUserId(), id, ct);
        if (request.Active == false && user.Active) await guard.EnsureNotLastAdministratorAsync(id, ct);

        if (request.Active is not null) user.Active = request.Active.Value;
        await db.SaveChangesAsync(ct);
        return Success(UserResponse.From(user));
    }

    // Kein "aktuelles Passwort" nötig; ausgestellte Tokens bleiben gültig.
    [HttpPatch("{id:guid}/password"), HasPrivilege<User>(CrudAction.Update)]
    public async Task<IActionResult> ResetPassword(Guid id, ResetPasswordRequest request, CancellationToken ct)
    {
        var user = await FindInScopeAsync(id, CrudAction.Update, ct);
        await guard.EnsureCoversUserAsync(User.GetUserId(), id, ct); // sonst: Passwort eines Stärkeren zurücksetzen = dessen Rechte übernehmen
        user.Password = passwords.Hash(request.Password!);
        await db.SaveChangesAsync(ct);
        return Success(UserResponse.From(user));
    }

    [HttpDelete("{id:guid}"), HasPrivilege<User>(CrudAction.Delete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var user = await FindInScopeAsync(id, CrudAction.Delete, ct);
        if (id == User.GetUserId()) throw ApiException.BadRequest(SelfDeactivation);
        await guard.EnsureCoversUserAsync(User.GetUserId(), id, ct);
        if (user.Active) await guard.EnsureNotLastAdministratorAsync(id, ct);

        user.Active = false;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // Heimatfiliale (optional): Scope "meine Filiale" für Zuweisungen mit homeScope. Wer sie setzt, muss die Filiale selbst abdecken.
    [HttpPut("{id:guid}/home-location"), HasPrivilege<User>(CrudAction.Update)]
    public async Task<IActionResult> SetHomeLocation(Guid id, SetHomeLocationRequest request, CancellationToken ct)
    {
        var user = await FindInScopeAsync(id, CrudAction.Update, ct);
        // Die Heimatfiliale verschiebt die Reichweite aller "homeScope"-Zuweisungen des Ziels: nur bei Usern, deren Rechte man selbst vollständig hat.
        await guard.EnsureCoversUserAsync(User.GetUserId(), id, ct);

        if (request.LocationId is { } locationId)
        {
            if (!await db.Locations.AnyAsync(l => l.Id == locationId, ct))
                throw ApiException.Validation("locationId", "Die Filiale wurde nicht gefunden.");
            if (!scope.Current.AllowsLocation(locationId)) throw AuthMessages.Forbidden(CrudAction.Update);
        }
        else if (!scope.Current.All && user.HomeLocationId is { } current && !scope.Current.AllowsLocation(current))
        {
            throw AuthMessages.Forbidden(CrudAction.Update);
        }

        user.HomeLocationId = request.LocationId;
        await db.SaveChangesAsync(ct);
        return Success(UserResponse.From(user));
    }

    [HttpGet("{id:guid}/roles"), HasPrivilege<AclRole>(CrudAction.Read)]
    public async Task<IActionResult> Roles(Guid id, CancellationToken ct)
    {
        await FindInScopeAsync(id, CrudAction.Read, ct);
        return Success(await AssignmentReader.LoadAsync(db, id, clock.GetUtcNow().UtcDateTime, onlyValid: false, ct));
    }

    // Setzt die Zuweisungen komplett neu. Jede hinzugefügte, geänderte oder entzogene Zuweisung muss der Handelnde samt Reichweite selbst abdecken
    // (sonst könnte er sich oder andere über eine stärkere Rolle oder einen größeren Scope hochstufen bzw. einem Administrator Rechte nehmen).
    [HttpPut("{id:guid}/roles"), HasPrivilege<AclRole>("assign")]
    public async Task<IActionResult> SetRoles(Guid id, SetUserRolesRequest request, CancellationToken ct)
    {
        var target = await FindInScopeAsync(id, CrudAction.Update, ct);
        var now = clock.GetUtcNow().UtcDateTime;

        if (request is { Assignments: not null, RoleIds: not null })
            throw ApiException.Validation("assignments", "Entweder assignments oder roleIds angeben, nicht beides.");
        var inputs = request.Assignments
                     ?? request.RoleIds?.Distinct().Select(roleId => new AssignmentInput { RoleId = roleId }).ToList()
                     ?? throw ApiException.Validation("assignments", "Dieser Wert darf nicht leer sein.");
        if (inputs.Any(i => i.RoleId is null)) throw ApiException.Validation("assignments", "Jede Zuweisung braucht eine Rolle.");

        var roleIds = inputs.Select(i => i.RoleId!.Value).ToList();
        if (roleIds.Distinct().Count() != roleIds.Count) throw ApiException.Validation("assignments", "Jede Rolle darf nur einmal zugewiesen werden.");
        var roles = await db.AclRoles.Where(r => roleIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);
        if (roles.Count != roleIds.Count) throw ApiException.Validation("assignments", "Eine oder mehrere Rollen wurden nicht gefunden.");

        foreach (var input in inputs)
        {
            if (!input.Scoped && (input.HomeScope || input.ScopeIds is { Count: > 0 }))
                throw ApiException.Validation("assignments", "scopeIds und homeScope gelten nur mit scoped = true.");
            if (input is { ValidFrom: { } from, ValidTo: { } to } && ToUtc(from) >= ToUtc(to))
                throw ApiException.Validation("assignments", "validTo muss nach validFrom liegen.");
        }

        var existing = await db.AclUserRoles.Include(x => x.Scopes).Where(x => x.UserId == id).ToListAsync(ct);
        var scopeIds = inputs.SelectMany(i => i.ScopeIds ?? []).Concat(existing.SelectMany(x => x.Scopes.Select(s => s.ScopeId))).Distinct().ToList();
        var scopes = await db.Scopes.Include(s => s.Members).Where(s => scopeIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        if (inputs.SelectMany(i => i.ScopeIds ?? []).Any(sid => !scopes.ContainsKey(sid)))
            throw ApiException.Validation("assignments", "Ein oder mehrere Scopes wurden nicht gefunden.");

        ScopeSet ScopeOf(bool scoped, bool homeScope, IEnumerable<Guid> ids)
        {
            if (!scoped) return ScopeSet.Everything;
            var members = ids.SelectMany(sid => scopes[sid].Members).ToList();
            return ScopeSet.Of(
                members.Where(m => m.Dimension == ScopeDimensions.Location).Select(m => m.RefId)
                    .Concat(homeScope && target.HomeLocationId is { } home ? [home] : []),
                members.Where(m => m.Dimension == ScopeDimensions.Report).Select(m => m.RefId));
        }

        var actor = User.GetUserId();
        foreach (var current in existing)
        {
            var input = inputs.FirstOrDefault(i => i.RoleId == current.AclRoleId);
            var rolePrivileges = await db.AclRoles.Where(r => r.Id == current.AclRoleId).Select(r => r.Privileges).FirstAsync(ct);
            var oldScope = ScopeOf(current.Scoped, current.HomeScope, current.Scopes.Select(s => s.ScopeId));
            if (input is null)
            {
                await guard.EnsureCoversAsync(actor, rolePrivileges, oldScope, ct);
                continue;
            }

            var newIds = (input.ScopeIds ?? []).Distinct().ToList();
            var unchanged = input.Scoped == current.Scoped && input.HomeScope == current.HomeScope
                            && NullableUtc(input.ValidFrom) == current.ValidFrom && NullableUtc(input.ValidTo) == current.ValidTo
                            && newIds.ToHashSet().SetEquals(current.Scopes.Select(s => s.ScopeId));
            if (unchanged) continue;
            await guard.EnsureCoversAsync(actor, rolePrivileges, oldScope, ct);
            var changedScope = ScopeOf(input.Scoped, input.HomeScope, newIds);
            await guard.EnsureCoversAsync(actor, roles[current.AclRoleId].Privileges, changedScope, ct);
            await guard.EnsureValidityWithinOwnAsync(actor, roles[current.AclRoleId].Privileges, changedScope, NullableUtc(input.ValidTo), ct);
        }

        foreach (var input in inputs.Where(i => existing.All(x => x.AclRoleId != i.RoleId)))
        {
            var addedScope = ScopeOf(input.Scoped, input.HomeScope, (input.ScopeIds ?? []).Distinct());
            await guard.EnsureCoversAsync(actor, roles[input.RoleId!.Value].Privileges, addedScope, ct);
            await guard.EnsureValidityWithinOwnAsync(actor, roles[input.RoleId!.Value].Privileges, addedScope, NullableUtc(input.ValidTo), ct);
        }

        var staysAdministrator = inputs.Any(i => !i.Scoped && roles[i.RoleId!.Value].Privileges.Contains(Privileges.Everything)
                                                 && (i.ValidFrom is null || ToUtc(i.ValidFrom.Value) <= now)
                                                 && i.ValidTo is null); // befristete Administratoren laufen ab und zählen nicht
        await guard.EnsureAdministratorsRemainAsync(id, staysAdministrator, ct);

        db.AclUserRoles.RemoveRange(existing.Where(x => inputs.All(i => i.RoleId != x.AclRoleId)));
        foreach (var input in inputs)
        {
            var newIds = (input.ScopeIds ?? []).Distinct().ToList();
            var assignment = existing.FirstOrDefault(x => x.AclRoleId == input.RoleId);
            if (assignment is null)
            {
                assignment = new AclUserRole { UserId = id, AclRoleId = input.RoleId!.Value };
                db.AclUserRoles.Add(assignment);
            }

            assignment.Scoped = input.Scoped;
            assignment.HomeScope = input.HomeScope;
            assignment.ValidFrom = NullableUtc(input.ValidFrom);
            assignment.ValidTo = NullableUtc(input.ValidTo);
            foreach (var stale in assignment.Scopes.Where(s => !newIds.Contains(s.ScopeId)).ToList()) assignment.Scopes.Remove(stale);
            foreach (var sid in newIds.Where(sid => assignment.Scopes.All(s => s.ScopeId != sid)))
                assignment.Scopes.Add(new AclUserRoleScope { UserId = id, AclRoleId = assignment.AclRoleId, ScopeId = sid });
        }

        await db.SaveChangesAsync(ct);
        return Success(await AssignmentReader.LoadAsync(db, id, now, onlyValid: false, ct));
    }

    // Außerhalb des eigenen Scopes: 403 mit der vordefinierten Meldung (der User existiert); nicht vorhanden: 404.
    private async Task<User> FindInScopeAsync(Guid id, CrudAction action, CancellationToken ct)
    {
        var user = await db.Users.InScope(db, scope.Current).FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is not null) return user;
        throw await db.Users.AnyAsync(u => u.Id == id, ct) ? AuthMessages.Forbidden(action) : ApiException.NotFound();
    }

    private static DateTime ToUtc(DateTime value) =>
        value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateTime? NullableUtc(DateTime? value) => value is null ? null : ToUtc(value.Value);
}
