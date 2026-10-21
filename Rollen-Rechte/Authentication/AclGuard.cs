using Rollen_Rechte.Common.Api;
using Rollen_Rechte.Data;
using Microsoft.EntityFrameworkCore;

namespace Rollen_Rechte.Authentication;

/// <summary>
/// Schutzregeln für die delegierte Rechteverwaltung: Niemand vergibt oder ändert mehr Rechte, als er selbst hat, und das System
/// behält immer mindestens einen aktiven Administrator (User mit einer gültigen, uneingeschränkten und unbefristeten Zuweisung einer Rolle, die <c>*</c> enthält; eine befristete läuft ab und zählt deshalb nicht).
/// </summary>
public sealed class AclGuard(AccessDbContext db, PrivilegeService privileges, PrivilegeCatalogService catalog, TimeProvider clock)
{
    public const string NoEscalation = "Sie dürfen nur Rechte vergeben, ändern oder entziehen, die Sie selbst haben.";
    public const string BuiltInRole = "Die Rolle \"Administrator\" ist eingebaut und kann nicht geändert oder gelöscht werden.";
    public const string ValidityTooLong = "Sie können Rechte nicht länger vergeben, als Sie sie selbst besitzen.";
    public const string LastAdministrator = "Es muss mindestens ein aktiver Benutzer mit der Rolle \"Administrator\" (alle Rechte) übrig bleiben.";

    /// <summary>Jedes Recht muss im Katalog stehen (generiert oder eigen; oder <c>*</c> / <c>bereich:*</c> sein); sonst 422.</summary>
    public async Task EnsureKnownAsync(IEnumerable<string> privilegeKeys, CancellationToken ct)
    {
        var snapshot = await catalog.GetAsync(ct);
        var unknown = privilegeKeys.FirstOrDefault(p => !snapshot.IsKnown(p));
        if (unknown is not null) throw ApiException.Validation("privileges", $"Unbekanntes Recht: {unknown}");
    }

    /// <summary>
    /// Der Handelnde muss jedes dieser Rechte <b>tatsächlich</b> haben (sonst 403): Platzhalter nur, wenn er sie selbst abdeckt, und jedes davon erfasste
    /// konkrete Recht mit Wirkung — ein Recht, das nur in einer nicht scopbaren Zuweisung mit Einschränkung steht, wirkt nicht und zählt nicht.
    /// Mit <paramref name="scope"/> muss seine Reichweite für jedes erfasste Recht die vergebene vollständig abdecken ("keine größere Reichweite
    /// vergeben als die eigene"); <see cref="ScopeSet.Everything"/> verlangt uneingeschränkte Wirkung (z. B. beim Ändern einer Rolle, die für alle ihre Inhaber gilt).
    /// </summary>
    public async Task EnsureCoversAsync(Guid actorId, IEnumerable<string> required, ScopeSet? scope, CancellationToken ct)
    {
        var list = required.ToList();
        var granted = await privileges.GetGrantedAsync(actorId, ct);
        if (list.Any(p => !Privileges.Allows(granted, p))) throw new ApiException(StatusCodes.Status403Forbidden, NoEscalation);

        foreach (var privilege in (await catalog.GetAsync(ct)).All.Where(c => list.Any(r => Privileges.Allows([r], c))))
        {
            var actorScope = await privileges.ScopeForAsync(actorId, privilege, ct);
            if (actorScope is null || (scope is not null && !await CoversAsync(actorScope, scope, ct)))
                throw new ApiException(StatusCodes.Status403Forbidden, NoEscalation);
        }
    }

    /// <summary>
    /// Wer einen anderen User verändert, der Rechte oder den Zugang betrifft (Passwort zurücksetzen, deaktivieren, Heimatfiliale), muss alle Rechte des
    /// Ziels samt Reichweite selbst abdecken — sonst könnte man sich über den Account eines Stärkeren hochstufen (Passwort zurücksetzen, einloggen).
    /// </summary>
    public async Task EnsureCoversUserAsync(Guid actorId, Guid targetUserId, CancellationToken ct)
    {
        if (actorId == targetUserId) return;
        foreach (var grant in await privileges.GetPotentialGrantsAsync(targetUserId, ct))
            await EnsureCoversAsync(actorId, grant.Privileges, grant.Scope, ct);
    }

    /// <summary>Wer Scope-Mitglieder ändert, ändert die Reichweite aller Zuweisungen mit diesem Scope: Er darf nur Mitglieder berühren, die er selbst abdeckt; sonst 403.</summary>
    public async Task EnsureReachCoversAsync(Guid actorId, ScopeSet touched, CancellationToken ct)
    {
        if (!await CoversAsync(await privileges.ReachAsync(actorId, ct), touched, ct)) throw new ApiException(StatusCodes.Status403Forbidden, NoEscalation);
    }
    public Task EnsureCoversAsync(Guid actorId, IEnumerable<string> required, CancellationToken ct) =>
        EnsureCoversAsync(actorId, required, null, ct);

    /// <summary>
    /// Eine vergebene Zuweisung darf nicht länger gelten als die eigene des Vergebenden: Läuft seine Berechtigung (z. B. als Vertretung) ab, darf
    /// nichts Unbefristetes oder Längeres von ihm übrig bleiben. Je betroffenem Recht zählt die längste eigene Zuweisung, die die Reichweite
    /// allein abdeckt (sonst die früheste); unbefristet ist unbegrenzt. Aufrufen nach <see cref="EnsureCoversAsync(Guid, IEnumerable{string}, ScopeSet?, CancellationToken)"/>.
    /// </summary>
    public async Task EnsureValidityWithinOwnAsync(Guid actorId, IEnumerable<string> required, ScopeSet scope, DateTime? validToUtc, CancellationToken ct)
    {
        var list = required.ToList();
        var grants = await privileges.GetGrantsAsync(actorId, ct);
        DateTime? cap = null;
        var snapshot = await catalog.GetAsync(ct);
        foreach (var privilege in snapshot.All.Where(c => list.Any(r => Privileges.Allows([r], c))))
        {
            var scopable = snapshot.IsScopable(privilege);
            var eligible = grants.Where(g => Privileges.Allows(g.Privileges, privilege) && (g.Scope.All || scopable)).ToList();
            var alone = eligible.Where(g => !scopable || g.Scope.Covers(scope)).ToList();
            DateTime? limit = alone.Count > 0
                ? (alone.Any(g => g.ValidTo is null) ? null : alone.Max(g => g.ValidTo))
                : eligible.Where(g => g.ValidTo is not null).Select(g => g.ValidTo).Min();
            if (limit is not null && (cap is null || limit < cap)) cap = limit;
        }

        if (cap is not null && (validToUtc is null || validToUtc > cap))
            throw new ApiException(StatusCodes.Status403Forbidden, ValidityTooLong);
    }

    public static bool IsBuiltIn(string roleName) => string.Equals(roleName.Trim(), AclSeeder.AdministratorRoleName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Deckt <paramref name="have"/> die Reichweite <paramref name="need"/> ab? Einzelmeldungen (Dimension "report") gehören der Anwendung, die sie führt:
    /// Dieser Dienst kennt ihre Filialen nicht und akzeptiert deshalb nur eine genau so schon vorhandene Freigabe. (Die Regel "eine Meldung der eigenen
    /// Region darf freigegeben werden" braucht eine Rückfrage an das Portal und kommt mit der Anbindung.)
    /// </summary>
    private static Task<bool> CoversAsync(ScopeSet have, ScopeSet need, CancellationToken ct) => Task.FromResult(have.Covers(need));
    /// <summary>Aktive User, die über eine Rolle mit <c>*</c> alles dürfen; optional ohne die Rolle <paramref name="exceptRoleId"/>.</summary>
    private async Task<HashSet<Guid>> ActiveAdministratorsAsync(Guid? exceptRoleId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var rows = await db.AclUserRoles.AsNoTracking()
            .Where(x => x.User.Active && !x.Scoped && (x.ValidFrom == null || x.ValidFrom <= now) && x.ValidTo == null
                        && (exceptRoleId == null || x.AclRoleId != exceptRoleId))
            .Select(x => new { x.UserId, x.AclRole.Privileges })
            .ToListAsync(ct);
        return rows.Where(r => r.Privileges.Contains(Privileges.Everything)).Select(r => r.UserId).ToHashSet();
    }

    /// <summary>User wird deaktiviert/gelöscht: er darf nicht der letzte aktive Administrator sein.</summary>
    public async Task EnsureNotLastAdministratorAsync(Guid userId, CancellationToken ct)
    {
        var admins = await ActiveAdministratorsAsync(null, ct);
        if (admins.Count == 1 && admins.Contains(userId)) throw ApiException.BadRequest(LastAdministrator);
    }

    /// <summary>Einem User werden die Rollen neu gesetzt; <paramref name="staysAdministrator"/> = hat danach noch eine Rolle mit <c>*</c>.</summary>
    public async Task EnsureAdministratorsRemainAsync(Guid userId, bool staysAdministrator, CancellationToken ct)
    {
        var admins = await ActiveAdministratorsAsync(null, ct);
        if (admins.Count == 0) return;
        var after = admins.Where(id => id != userId).Count() + (staysAdministrator && admins.Contains(userId) ? 1 : 0);
        if (after == 0) throw ApiException.BadRequest(LastAdministrator);
    }

    /// <summary>Die Rolle verliert <c>*</c> oder wird gelöscht: Es muss ein anderer aktiver Administrator bleiben.</summary>
    public async Task EnsureAdministratorsRemainWithoutRoleAsync(Guid roleId, CancellationToken ct)
    {
        if ((await ActiveAdministratorsAsync(null, ct)).Count == 0) return;
        if ((await ActiveAdministratorsAsync(roleId, ct)).Count == 0) throw ApiException.BadRequest(LastAdministrator);
    }
}
