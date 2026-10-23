using Access.Core;
using Access.Core.Acl;
using Access.Core.Publishing;
using Access.Core.Responsibilities;
using Access.Infrastructure.Publishing;
using Microsoft.EntityFrameworkCore;

namespace Access.Infrastructure.Acl;

/// <summary>Result of an administrative change; <see cref="Id"/> is set for created items.</summary>
public sealed record AdminResult(GuardResult Guard, Guid? Id = null)
{
    public static AdminResult Denied(GuardReason reason) => new(GuardResult.Denied(reason));
}

/// <summary>
/// Administration of the ACL. Every change is checked by the protection rules (<see cref="AclGuard"/>) for the acting person,
/// then saved, recalculated and published in one transaction (<see cref="RecalculationRunner"/>).
/// </summary>
public sealed class AclAdministration(AccessDbContext db, IAclReader reader, RecalculationRunner runner, TimeProvider time, AccessOptions options)
{
    public const string CustomOwner = "custom";

    public async Task<AdminResult> CreateRoleAsync(Guid actor, string name, IReadOnlySet<string> privileges, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return AdminResult.Denied(GuardReason.Invalid);
        }

        if ((await db.Roles.Select(r => r.Name).ToListAsync(cancellationToken)).Any(n => SameName(n, name)))
        {
            return AdminResult.Denied(GuardReason.InUse);
        }

        var guard = (await Guard(cancellationToken)).CanCreateRole(actor, privileges);
        var id = NewId();
        return await Apply(guard, id, _ =>
        {
            db.Roles.Add(new RoleRecord { Id = id, Name = name.Trim(), Privileges = [.. privileges] });
            return Task.CompletedTask;
        }, cancellationToken);
    }

    public async Task<AdminResult> UpdateRoleAsync(Guid actor, Guid roleId, IReadOnlySet<string> privileges, CancellationToken cancellationToken)
    {
        var guard = (await Guard(cancellationToken)).CanChangeRole(actor, roleId, privileges);
        return await Apply(guard, roleId, async ct =>
        {
            var role = await db.Roles.SingleAsync(r => r.Id == roleId, ct);
            role.Privileges = [.. privileges];
        }, cancellationToken);
    }

    public async Task<AdminResult> DeleteRoleAsync(Guid actor, Guid roleId, CancellationToken cancellationToken)
    {
        var guard = (await Guard(cancellationToken)).CanDeleteRole(actor, roleId);
        return await Apply(guard, roleId, ct => db.Roles.Where(r => r.Id == roleId).ExecuteDeleteAsync(ct), cancellationToken);
    }

    public async Task<AdminResult> CreateScopeAsync(Guid actor, string name, IReadOnlySet<Guid> storeIds, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return AdminResult.Denied(GuardReason.Invalid);
        }

        if ((await db.Scopes.Select(s => s.Name).ToListAsync(cancellationToken)).Any(n => SameName(n, name)))
        {
            return AdminResult.Denied(GuardReason.InUse);
        }

        var guard = (await Guard(cancellationToken)).CanUse(actor, AccessPrivileges.ScopeCreate);
        var id = NewId();
        return await Apply(guard, id, _ =>
        {
            db.Scopes.Add(new ScopeRecord { Id = id, Name = name.Trim(), StoreIds = [.. storeIds] });
            return Task.CompletedTask;
        }, cancellationToken);
    }

    public async Task<AdminResult> UpdateScopeAsync(Guid actor, Guid scopeId, IReadOnlySet<Guid> storeIds, CancellationToken cancellationToken)
    {
        if (!await db.Scopes.AnyAsync(s => s.Id == scopeId, cancellationToken))
        {
            return AdminResult.Denied(GuardReason.NotFound);
        }

        var guard = (await Guard(cancellationToken)).CanChangeScope(actor, scopeId, storeIds);
        return await Apply(guard, scopeId, async ct =>
        {
            var scope = await db.Scopes.SingleAsync(s => s.Id == scopeId, ct);
            scope.StoreIds = [.. storeIds];
        }, cancellationToken);
    }

    public async Task<AdminResult> DeleteScopeAsync(Guid actor, Guid scopeId, CancellationToken cancellationToken)
    {
        if (!await db.Scopes.AnyAsync(s => s.Id == scopeId, cancellationToken))
        {
            return AdminResult.Denied(GuardReason.NotFound);
        }

        var guard = (await Guard(cancellationToken)).CanDeleteScope(actor, scopeId);
        return await Apply(guard, scopeId, ct => db.Scopes.Where(s => s.Id == scopeId).ExecuteDeleteAsync(ct), cancellationToken);
    }

    public async Task<AdminResult> AssignAsync(
        Guid actor, Guid employeeId, Guid roleId, AssignmentReach reach, DateTimeOffset? validFrom, DateTimeOffset? validTo, CancellationToken cancellationToken)
    {
        if (validFrom is not null && validTo is not null && validTo <= validFrom)
        {
            return AdminResult.Denied(GuardReason.Invalid);
        }

        var data = await reader.LoadAsync(cancellationToken);
        if (reach.ScopeIds.Any(id => data.Scopes.All(s => s.Id != id)))
        {
            return AdminResult.Denied(GuardReason.NotFound);
        }

        var guard = NewGuard(data).CanAssign(actor, employeeId, roleId, reach, validTo);
        var id = NewId();
        return await Apply(guard, id, _ =>
        {
            db.Assignments.Add(new AssignmentRecord
            {
                Id = id,
                EmployeeId = employeeId,
                RoleId = roleId,
                Unrestricted = reach.Unrestricted,
                IncludeHomeStore = reach.IncludeHomeStore,
                ScopeIds = [.. reach.ScopeIds],
                ValidFrom = validFrom,
                ValidTo = validTo,
                Source = nameof(AssignmentSource.Manual),
            });
            return Task.CompletedTask;
        }, cancellationToken);
    }

    public async Task<AdminResult> RemoveAssignmentAsync(Guid actor, Guid assignmentId, CancellationToken cancellationToken)
    {
        var guard = (await Guard(cancellationToken)).CanRemoveAssignment(actor, assignmentId);
        return await Apply(guard, assignmentId, ct => db.Assignments.Where(a => a.Id == assignmentId).ExecuteDeleteAsync(ct), cancellationToken);
    }

    public async Task<AdminResult> AddResponsibilityAsync(
        Guid actor, string topic, Guid responsibleEmployeeId, ResponsibilityTarget target, DateTimeOffset? validFrom, DateTimeOffset? validTo,
        CancellationToken cancellationToken)
    {
        if (!Responsibility.IsValidTopic(topic) || (validFrom is not null && validTo is not null && validTo <= validFrom))
        {
            return AdminResult.Denied(GuardReason.Invalid);
        }

        if (!await db.People.AnyAsync(p => p.EmployeeId == responsibleEmployeeId, cancellationToken))
        {
            return AdminResult.Denied(GuardReason.NotFound);
        }

        var guard = (await Guard(cancellationToken)).CanSetResponsibility(actor, target);
        var id = NewId();
        var (kind, targetId) = EfAclReader.FromTarget(target);
        return await Apply(guard, id, _ =>
        {
            db.Responsibilities.Add(new ResponsibilityRecord
            {
                Id = id,
                Topic = topic,
                ResponsibleEmployeeId = responsibleEmployeeId,
                TargetKind = kind,
                TargetId = targetId,
                ValidFrom = validFrom,
                ValidTo = validTo,
            });
            return Task.CompletedTask;
        }, cancellationToken);
    }

    public async Task<AdminResult> RemoveResponsibilityAsync(Guid actor, Guid responsibilityId, CancellationToken cancellationToken)
    {
        var record = await db.Responsibilities.AsNoTracking().FirstOrDefaultAsync(r => r.Id == responsibilityId, cancellationToken);
        if (record is null)
        {
            return AdminResult.Denied(GuardReason.NotFound);
        }

        var guard = (await Guard(cancellationToken)).CanSetResponsibility(actor, EfAclReader.ToResponsibility(record).Target, AccessPrivileges.ResponsibilityDelete);
        return await Apply(guard, responsibilityId, ct => db.Responsibilities.Where(r => r.Id == responsibilityId).ExecuteDeleteAsync(ct), cancellationToken);
    }

    public async Task<AdminResult> CreateCustomPrivilegeAsync(Guid actor, string key, bool scopable, CancellationToken cancellationToken)
    {
        if (!PrivilegePattern.IsValidPattern(key) || key.EndsWith('*'))
        {
            return AdminResult.Denied(GuardReason.Invalid);
        }

        if (await db.Catalog.AnyAsync(c => c.Key == key, cancellationToken))
        {
            return AdminResult.Denied(GuardReason.InUse);
        }

        var guard = (await Guard(cancellationToken)).CanUse(actor, AccessPrivileges.CustomPrivilegeCreate);
        return await Apply(guard, null, _ =>
        {
            db.Catalog.Add(new CatalogEntryRecord { Key = key, Owner = CustomOwner, Scopable = scopable });
            return Task.CompletedTask;
        }, cancellationToken);
    }

    public async Task<AdminResult> DeleteCustomPrivilegeAsync(Guid actor, string key, CancellationToken cancellationToken)
    {
        if (!await db.Catalog.AnyAsync(c => c.Key == key && c.Owner == CustomOwner, cancellationToken))
        {
            return AdminResult.Denied(GuardReason.NotFound);
        }

        var guard = (await Guard(cancellationToken)).CanUse(actor, AccessPrivileges.CustomPrivilegeDelete);
        return await Apply(guard, null, ct => db.Catalog.Where(c => c.Key == key).ExecuteDeleteAsync(ct), cancellationToken);
    }

    /// <summary>
    /// An application registers its privileges (service-to-service). Replaces the application's previous catalogue; a key belongs to
    /// exactly one application, Access's own and custom privileges are reserved. Privileges that disappear stop taking effect.
    /// </summary>
    public async Task<AdminResult> RegisterCatalogAsync(string appKey, IReadOnlyList<CatalogEntry> entries, CancellationToken cancellationToken)
    {
        var reserved = new[] { AccessPrivileges.Owner, CustomOwner };
        if (reserved.Contains(appKey) || !PrivilegePattern.IsValidPattern(appKey + ":x")
            || entries.Any(e => !PrivilegePattern.IsValidPattern(e.Key) || e.Key.EndsWith('*'))
            || entries.Select(e => e.Key).Distinct(StringComparer.Ordinal).Count() != entries.Count)
        {
            return AdminResult.Denied(GuardReason.Invalid);
        }

        var keys = entries.Select(e => e.Key).ToList();
        if (await db.Catalog.AnyAsync(c => keys.Contains(c.Key) && c.Owner != appKey, cancellationToken))
        {
            return AdminResult.Denied(GuardReason.InUse);
        }

        return await Apply(GuardResult.Allowed, null, async ct =>
        {
            await db.Catalog.Where(c => c.Owner == appKey).ExecuteDeleteAsync(ct);
            db.Catalog.AddRange(entries.Select(e => new CatalogEntryRecord { Key = e.Key, Owner = appKey, Scopable = e.Scopable }));
        }, cancellationToken);
    }

    private async Task<AdminResult> Apply(GuardResult guard, Guid? id, Func<CancellationToken, Task> change, CancellationToken cancellationToken)
    {
        if (!guard.IsAllowed)
        {
            return new AdminResult(guard);
        }

        await runner.RunAsync(change, cancellationToken);
        return new AdminResult(GuardResult.Allowed, id);
    }

    private async Task<AclGuard> Guard(CancellationToken cancellationToken) => NewGuard(await reader.LoadAsync(cancellationToken));

    private AclGuard NewGuard(AclData data) => new(data, time.GetUtcNow(), options.Zone);

    private Guid NewId() => Guid.CreateVersion7(time.GetUtcNow());

    /// <summary>Names are unique regardless of case and surrounding spaces (e.g. "administrator" is taken).</summary>
    private static bool SameName(string existing, string requested) => string.Equals(existing, requested.Trim(), StringComparison.OrdinalIgnoreCase);
}
