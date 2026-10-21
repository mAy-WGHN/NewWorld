using Rollen_Rechte.Domain.Entities;
using Rollen_Rechte.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Rollen_Rechte.Authentication;

public static class AuthPolicies
{
    /// <summary>Nur angemeldet, kein bestimmtes Recht (z. B. eigenes Passwort, eigene Rechte).</summary>
    public const string Authenticated = "authenticated";

    public const string PrivilegePrefix = "privilege:";

    /// <summary>Recht aus der Tabelle <c>custom_privilege</c> (zur Laufzeit angelegt); die Prüfung verlangt zusätzlich, dass es existiert.</summary>
    public const string CustomPrefix = "custom-privilege:";
}

/// <summary>Verlangt ein Recht aus dem Katalog <see cref="Privileges"/>; fehlt es, antwortet die API mit 403.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class HasPrivilegeAttribute(string privilege) : AuthorizeAttribute(AuthPolicies.PrivilegePrefix + privilege);

/// <summary>
/// Verlangt ein Recht der Ressource <typeparamref name="TEntity"/> (Entity mit <see cref="PrivilegeResourceAttribute"/>):
/// <c>[HasPrivilege&lt;Report&gt;(CrudAction.Read)]</c> für <c>report:read</c>, <c>[HasPrivilege&lt;AclRole&gt;("assign")]</c> für eine Zusatzaktion.
/// Eine nicht freigegebene Aktion fällt beim Start auf (Exception), nicht erst im Betrieb.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class HasPrivilegeAttribute<TEntity> : AuthorizeAttribute
{
    public HasPrivilegeAttribute(CrudAction action)
        : base(AuthPolicies.PrivilegePrefix + PrivilegeCatalog.PrivilegeOf(typeof(TEntity), action)) { }

    public HasPrivilegeAttribute(string action)
        : base(AuthPolicies.PrivilegePrefix + PrivilegeCatalog.PrivilegeOf(typeof(TEntity), action)) { }
}

/// <summary>
/// Verlangt <see cref="Privilege"/>; bei <see cref="Custom"/> muss es außerdem als eigenes Recht existieren
/// (gelöscht = niemand darf mehr, auch nicht über eine veraltete Rolle).
/// </summary>
public sealed record PrivilegeRequirement(string Privilege, bool Custom = false) : IAuthorizationRequirement;

/// <summary>
/// Verlangt ein zur Laufzeit angelegtes Recht (<see cref="Domain.Entities.CustomPrivilege"/>), z. B. <c>[HasCustomPrivilege("report:export")]</c>.
/// Das Recht muss in <c>custom_privilege</c> existieren und über eine Rolle vergeben sein (oder durch <c>*</c>/<c>report:*</c> gedeckt werden).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class HasCustomPrivilegeAttribute(string privilege) : AuthorizeAttribute(AuthPolicies.CustomPrefix + privilege);

/// <summary>Baut die Policy <c>privilege:&lt;recht&gt;</c> bei Bedarf, alle anderen Namen löst der Standard auf.</summary>
public sealed class PrivilegePolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        PrivilegeRequirement? requirement = null;
        if (policyName.StartsWith(AuthPolicies.PrivilegePrefix, StringComparison.Ordinal))
            requirement = new PrivilegeRequirement(policyName[AuthPolicies.PrivilegePrefix.Length..]);
        else if (policyName.StartsWith(AuthPolicies.CustomPrefix, StringComparison.Ordinal))
            requirement = new PrivilegeRequirement(policyName[AuthPolicies.CustomPrefix.Length..], Custom: true);

        if (requirement is null) return await base.GetPolicyAsync(policyName);
        return new AuthorizationPolicyBuilder().RequireAuthenticatedUser().AddRequirements(requirement).Build();
    }
}

/// <summary>Eine gültige Rollenzuweisung eines Users: die Rechte der Rolle und die Reichweite, in der sie gelten.</summary>
public sealed record Grant(Guid RoleId, string RoleName, IReadOnlyList<string> Privileges, ScopeSet Scope, DateTime? ValidTo);

/// <summary>
/// Rechte eines Users: die Vereinigung der Rechte aller seiner <b>gültigen</b> (zeitlich nicht abgelaufenen) Rollenzuweisungen, je Request und User
/// einmal geladen. Eine Zuweisung kann auf einen Scope beschränkt sein; für Ressourcen ohne <c>Scopable</c> zählen nur uneingeschränkte Zuweisungen.
/// </summary>
public sealed class PrivilegeService(AccessDbContext db, TimeProvider clock, PrivilegeCatalogService catalog)
{
    private Guid _cachedFor;
    private List<Grant>? _grants;

    /// <summary>Die jetzt gültigen Zuweisungen (je Request und User einmal geladen).</summary>
    public async Task<IReadOnlyList<Grant>> GetGrantsAsync(Guid userId, CancellationToken ct = default)
    {
        if (_grants is not null && _cachedFor == userId) return _grants;

        _grants = await LoadAsync(userId, includeNotYetValid: false, ct);
        _cachedFor = userId;
        return _grants;
    }

    /// <summary>Alle noch nicht abgelaufenen Zuweisungen, auch die erst künftig gültigen (für Schutzprüfungen am Ziel: Es zählt, was der User bekommen wird).</summary>
    public Task<List<Grant>> GetPotentialGrantsAsync(Guid userId, CancellationToken ct = default) =>
        LoadAsync(userId, includeNotYetValid: true, ct);

    private async Task<List<Grant>> LoadAsync(Guid userId, bool includeNotYetValid, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var rows = await db.AclUserRoles.AsNoTracking()
            .Where(x => x.UserId == userId && (includeNotYetValid || x.ValidFrom == null || x.ValidFrom <= now) && (x.ValidTo == null || x.ValidTo > now))
            .Select(x => new { x.AclRoleId, x.AclRole.Name, x.AclRole.Privileges, x.Scoped, x.HomeScope, x.ValidTo })
            .ToListAsync(ct);

        var members = rows.Any(r => r.Scoped)
            ? await db.AclUserRoleScopes.AsNoTracking().Where(s => s.UserId == userId)
                .SelectMany(s => s.Scope.Members.Select(m => new { s.AclRoleId, m.Dimension, m.RefId }))
                .ToListAsync(ct)
            : [];
        var home = rows.Any(r => r is { Scoped: true, HomeScope: true })
            ? await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.HomeLocationId).FirstOrDefaultAsync(ct)
            : null;

        return rows.Select(r =>
        {
            var scope = !r.Scoped
                ? ScopeSet.Everything
                : ScopeSet.Of(
                    members.Where(m => m.AclRoleId == r.AclRoleId && m.Dimension == ScopeDimensions.Location).Select(m => m.RefId)
                        .Concat(r.HomeScope && home is { } h ? [h] : []),
                    members.Where(m => m.AclRoleId == r.AclRoleId && m.Dimension == ScopeDimensions.Report).Select(m => m.RefId));
            return new Grant(r.AclRoleId, r.Name, r.Privileges, scope, r.ValidTo);
        }).ToList();
    }

    /// <summary>Die Datenreichweite des Users: Vereinigung der Scopes seiner gültigen Zuweisungen mit Daten-Rechten (nicht der reinen Rollen-/Scope-Verwaltung); leer, wenn er keine hat.</summary>
    public async Task<ScopeSet> ReachAsync(Guid userId, CancellationToken ct = default)
    {
        var snapshot = await catalog.GetAsync(ct);
        var reach = ScopeSet.Of([], []);
        foreach (var grant in (await GetGrantsAsync(userId, ct)).Where(g => snapshot.DefinesReach(g.Privileges))) reach = reach.Union(grant.Scope);
        return reach;
    }
    /// <summary>Die gespeicherten Rechte (auch Platzhalter wie "*" oder "report:*") aller gültigen Zuweisungen, unabhängig vom Scope.</summary>
    public async Task<IReadOnlyList<string>> GetGrantedAsync(Guid userId, CancellationToken ct = default) =>
        (await GetGrantsAsync(userId, ct)).SelectMany(g => g.Privileges).Distinct().ToList();

    /// <summary>
    /// Die Reichweite, in der <paramref name="userId"/> das Recht hat: <c>null</c> = hat es nicht; <see cref="ScopeSet.All"/> = überall;
    /// sonst die Vereinigung der Scopes aller Zuweisungen, deren Rolle es enthält. Bei nicht scopbaren Rechten zählen nur uneingeschränkte Zuweisungen.
    /// </summary>
    public async Task<ScopeSet?> ScopeForAsync(Guid userId, string privilege, CancellationToken ct = default)
    {
        var scopable = (await catalog.GetAsync(ct)).IsScopable(privilege);
        ScopeSet? result = null;
        foreach (var grant in await GetGrantsAsync(userId, ct))
        {
            if (!Privileges.Allows(grant.Privileges, privilege)) continue;
            if (grant.Scope.All) return ScopeSet.Everything;
            if (scopable) result = result?.Union(grant.Scope) ?? grant.Scope;
        }

        return result;
    }

    public async Task<bool> HasAsync(Guid userId, string privilege, CancellationToken ct = default) =>
        await ScopeForAsync(userId, privilege, ct) is not null;
}

public sealed class PrivilegeHandler(PrivilegeService privileges, PrivilegeCatalogService catalog, ScopeContext scopeContext, IHttpContextAccessor accessor) : AuthorizationHandler<PrivilegeRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PrivilegeRequirement requirement)
    {
        if (!Guid.TryParse(context.User.FindFirst("sub")?.Value, out var userId)) return;

        var ct = accessor.HttpContext?.RequestAborted ?? default;
        if (requirement.Custom && !(await catalog.GetAsync(ct)).Custom.Contains(requirement.Privilege)) return;

        // Der Scope zum verlangten Recht gilt für die Abfragen dieser Anfrage (z. B. Meldungen).
        if (await privileges.ScopeForAsync(userId, requirement.Privilege, ct) is { } scope)
        {
            scopeContext.Current = scope;
            context.Succeed(requirement);
        }
    }
}
