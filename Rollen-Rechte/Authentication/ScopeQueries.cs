using Rollen_Rechte.Contracts;
using Rollen_Rechte.Data;
using Rollen_Rechte.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Rollen_Rechte.Authentication;

/// <summary>Anwendung eines <see cref="ScopeSet"/> auf Abfragen. Meldungen filtert der DbContext selbst (globaler Filter); hier der Rest.</summary>
public static class ScopeQueries
{
    /// <summary>Filialen im Scope.</summary>
    public static IQueryable<Location> InScope(this IQueryable<Location> locations, ScopeSet scope)
    {
        if (scope.All) return locations;
        var ids = scope.Locations.ToArray();
        return locations.Where(l => ids.Contains(l.Id));
    }

    /// <summary>
    /// User im Scope: Heimatfiliale in einer der Filialen oder eine ihrer eingeschränkten Zuweisungen mit einer dieser Filialen
    /// ("hierarchisch unter mir"). User ohne jeden Bezug sind nur für uneingeschränkte Zuweisungen sichtbar.
    /// </summary>
    public static IQueryable<User> InScope(this IQueryable<User> users, AccessDbContext db, ScopeSet scope)
    {
        if (scope.All) return users;
        var ids = scope.Locations.ToArray();
        return users.Where(u =>
            (u.HomeLocationId != null && ids.Contains(u.HomeLocationId.Value))
            || db.AclUserRoleScopes.Any(s => s.UserId == u.Id
                && s.Scope.Members.Any(m => m.Dimension == ScopeDimensions.Location && ids.Contains(m.RefId))));
    }
}

/// <summary>Liest die Rollenzuweisungen eines Users (mit Scopes und Gültigkeit) für die API-Antworten.</summary>
public static class AssignmentReader
{
    public static async Task<List<AssignmentResponse>> LoadAsync(AccessDbContext db, Guid userId, DateTime utcNow, bool onlyValid, CancellationToken ct)
    {
        var rows = await db.AclUserRoles.AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.AclRole.Name)
            .Select(x => new
            {
                x.AclRoleId, x.AclRole.Name, x.Scoped, x.HomeScope, x.ValidFrom, x.ValidTo,
                Scopes = x.Scopes.Select(s => new { s.ScopeId, s.Scope.Name }).ToList()
            })
            .ToListAsync(ct);

        return rows
            .Select(r => new AssignmentResponse(
                r.AclRoleId, r.Name, r.Scoped, r.HomeScope,
                r.Scopes.OrderBy(s => s.Name).Select(s => new ScopeRef(s.ScopeId, s.Name)).ToList(),
                r.ValidFrom, r.ValidTo,
                (r.ValidFrom is null || r.ValidFrom <= utcNow) && (r.ValidTo is null || r.ValidTo > utcNow)))
            .Where(a => !onlyValid || a.Valid)
            .ToList();
    }
}
