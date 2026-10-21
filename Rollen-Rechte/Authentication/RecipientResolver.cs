using Microsoft.EntityFrameworkCore;
using Rollen_Rechte.Data;
using Rollen_Rechte.Domain.Entities;

namespace Rollen_Rechte.Authentication;

/// <summary>
/// Beantwortet für andere Anwendungen (z. B. den Benachrichtigungs-Dienst): "Welche aktiven User dürfen <paramref name="privilege"/> in Bezug auf diese Filialen /
/// Einzelobjekte?" — also wer eine Nachricht zu einem Vorgang überhaupt sehen darf. Dieselben Regeln wie bei der Rechteprüfung eines einzelnen Users:
/// nur aktive User, nur gültige (nicht abgelaufene, schon begonnene) Zuweisungen, uneingeschränkte Zuweisungen immer, eingeschränkte nur bei scopbaren Rechten und
/// wenn eine der Filialen (<c>eine genügt</c>), die Heimatfiliale bei <c>HomeScope</c> oder eine der Einzelobjekte im Scope liegt.
/// </summary>
public sealed class RecipientResolver(AccessDbContext db, PrivilegeCatalogService catalog, TimeProvider clock)
{
    public async Task<IReadOnlyList<Guid>> ResolveAsync(
        string privilege, IReadOnlyCollection<Guid> locationIds, IReadOnlyCollection<Guid> reportIds, IReadOnlyCollection<Guid>? userIds, CancellationToken ct)
    {
        var scopable = (await catalog.GetAsync(ct)).IsScopable(privilege);
        var now = clock.GetUtcNow().UtcDateTime;
        var onlyUsers = userIds?.Distinct().ToArray();

        var query = db.AclUserRoles.AsNoTracking()
            .Where(x => x.User.Active && (x.ValidFrom == null || x.ValidFrom <= now) && (x.ValidTo == null || x.ValidTo > now));
        if (onlyUsers is not null) query = query.Where(x => onlyUsers.Contains(x.UserId));

        var grants = (await query
                .Select(x => new { x.UserId, x.AclRoleId, x.AclRole.Privileges, x.Scoped, x.HomeScope, Home = x.User.HomeLocationId })
                .ToListAsync(ct))
            .Where(g => Privileges.Allows(g.Privileges, privilege))
            .ToList();

        var result = grants.Where(g => !g.Scoped).Select(g => g.UserId).ToHashSet();

        // Eingeschränkte Zuweisungen zählen nur bei scopbaren Rechten, und nur, wenn der Vorgang zu einer Filiale/einem Einzelobjekt im Scope gehört.
        var scoped = scopable ? grants.Where(g => g.Scoped && !result.Contains(g.UserId)).ToList() : [];
        if (scoped.Count == 0 || (locationIds.Count == 0 && reportIds.Count == 0)) return result.Order().ToList();

        var scopedUsers = scoped.Select(g => g.UserId).Distinct().ToArray();
        var members = await db.AclUserRoleScopes.AsNoTracking()
            .Where(s => scopedUsers.Contains(s.UserId))
            .SelectMany(s => s.Scope.Members.Select(m => new { s.UserId, s.AclRoleId, m.Dimension, m.RefId }))
            .ToListAsync(ct);

        foreach (var grant in scoped)
        {
            var own = members.Where(m => m.UserId == grant.UserId && m.AclRoleId == grant.AclRoleId).ToList();
            var locations = own.Where(m => m.Dimension == ScopeDimensions.Location).Select(m => m.RefId).ToHashSet();
            if (grant.HomeScope && grant.Home is { } home) locations.Add(home);
            var reports = own.Where(m => m.Dimension == ScopeDimensions.Report).Select(m => m.RefId).ToHashSet();

            if (locationIds.Any(locations.Contains) || reportIds.Any(reports.Contains)) result.Add(grant.UserId);
        }

        return result.Order().ToList();
    }
}
