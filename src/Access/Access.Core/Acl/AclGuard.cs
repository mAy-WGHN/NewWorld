using Access.Core.Responsibilities;

namespace Access.Core.Acl;

public enum GuardReason
{
    None = 0,

    /// <summary>The target person, store or scope is outside the actor's reach for the management privilege.</summary>
    NotInReach = 1,

    /// <summary>The actor would grant privileges or reach they do not have themselves.</summary>
    NoEscalation = 2,

    /// <summary>The actor would grant longer than their own privileges last.</summary>
    ValidityTooLong = 3,

    /// <summary>Built-in roles (Administrator; Public User cannot be deleted).</summary>
    BuiltInRole = 4,

    /// <summary>At least one active, permanent, unrestricted administrator must remain.</summary>
    LastAdministrator = 5,

    /// <summary>Privilege pattern not in the catalogue.</summary>
    UnknownPrivilege = 6,

    /// <summary>The role or scope is still assigned.</summary>
    InUse = 7,

    /// <summary>The referenced role, scope or assignment does not exist.</summary>
    NotFound = 8,

    /// <summary>Malformed input (name, privilege key, topic, period).</summary>
    Invalid = 9,
}

public sealed record GuardResult(bool IsAllowed, GuardReason Reason)
{
    public static readonly GuardResult Allowed = new(true, GuardReason.None);

    public static GuardResult Denied(GuardReason reason) => new(false, reason);
}

/// <summary>
/// Protection rules of the delegated administration (port of the Rollen-Rechte AclGuard): nobody grants, changes or removes more
/// privileges, reach or validity than they effectively have themselves; changing a role requires covering it everywhere (it applies
/// to all its holders); the built-in administrator role is fixed; one active, permanent, unrestricted administrator always remains.
/// </summary>
public sealed class AclGuard(AclData data, DateTimeOffset now, TimeZoneInfo zone)
{
    private readonly EffectivePermissionCalculator.Context _context = new(data, now, zone);

    public GuardResult CanAssign(Guid actor, Guid target, Guid roleId, AssignmentReach reach, DateTimeOffset? validTo)
    {
        var actorPermissions = Permissions(actor);
        if (!actorPermissions.AppliesToEmployee(AccessPrivileges.AssignmentCreate, target) || Person(target) is not { } targetPerson)
        {
            return GuardResult.Denied(GuardReason.NotInReach);
        }

        if (!_context.RolesById.TryGetValue(roleId, out var role))
        {
            return GuardResult.Denied(GuardReason.NotFound);
        }

        var requestedStores = _context.StoresOf(reach, targetPerson.HomeStoreId);
        var keys = PrivilegePattern.Expand(role.Privileges, _context.CatalogKeys);
        if (!Covers(actorPermissions, keys, requestedStores))
        {
            return GuardResult.Denied(GuardReason.NoEscalation);
        }

        return keys.All(key => OwnValidityCovers(actor, key, requestedStores, validTo))
            ? GuardResult.Allowed
            : GuardResult.Denied(GuardReason.ValidityTooLong);
    }

    public GuardResult CanRemoveAssignment(Guid actor, Guid assignmentId)
    {
        var assignment = data.Assignments.FirstOrDefault(a => a.Id == assignmentId);
        if (assignment is null || !_context.RolesById.TryGetValue(assignment.RoleId, out var role))
        {
            return GuardResult.Denied(GuardReason.NotFound);
        }

        var actorPermissions = Permissions(actor);
        if (!actorPermissions.AppliesToEmployee(AccessPrivileges.AssignmentDelete, assignment.EmployeeId))
        {
            return GuardResult.Denied(GuardReason.NotInReach);
        }

        var stores = _context.StoresOf(assignment.Reach, Person(assignment.EmployeeId)?.HomeStoreId);
        if (!Covers(actorPermissions, PrivilegePattern.Expand(role.Privileges, _context.CatalogKeys), stores))
        {
            return GuardResult.Denied(GuardReason.NoEscalation);
        }

        return AdministratorsRemain(data with { Assignments = data.Assignments.Where(a => a.Id != assignmentId).ToList() })
            ? GuardResult.Allowed
            : GuardResult.Denied(GuardReason.LastAdministrator);
    }

    public GuardResult CanCreateRole(Guid actor, IReadOnlySet<string> privileges) =>
        RoleChange(actor, AccessPrivileges.RoleCreate, new HashSet<string>(), privileges, data);

    public GuardResult CanChangeRole(Guid actor, Guid roleId, IReadOnlySet<string> privileges)
    {
        if (!_context.RolesById.TryGetValue(roleId, out var role))
        {
            return GuardResult.Denied(GuardReason.NotFound);
        }

        if (role.Id == Role.AdministratorId)
        {
            return GuardResult.Denied(GuardReason.BuiltInRole);
        }

        var after = data with { Roles = data.Roles.Select(r => r.Id == roleId ? r with { Privileges = privileges } : r).ToList() };
        return RoleChange(actor, AccessPrivileges.RoleUpdate, role.Privileges, privileges, after);
    }

    public GuardResult CanDeleteRole(Guid actor, Guid roleId)
    {
        if (!_context.RolesById.TryGetValue(roleId, out var role))
        {
            return GuardResult.Denied(GuardReason.NotFound);
        }

        if (role.BuiltIn)
        {
            return GuardResult.Denied(GuardReason.BuiltInRole);
        }

        if (data.Assignments.Any(a => a.RoleId == roleId))
        {
            return GuardResult.Denied(GuardReason.InUse);
        }

        return RoleChange(actor, AccessPrivileges.RoleDelete, role.Privileges, new HashSet<string>(), data);
    }

    /// <summary>Creating roles' building blocks that grant nothing by themselves (scopes, own privileges) needs the privilege only.</summary>
    public GuardResult CanUse(Guid actor, string privilege) =>
        Permissions(actor).Privileges.TryGetValue(privilege, out var reach) && reach.Unrestricted
            ? GuardResult.Allowed
            : GuardResult.Denied(GuardReason.NoEscalation);

    /// <summary>Changing scope members changes the reach of every assignment using the scope: only stores the actor reaches.</summary>
    public GuardResult CanChangeScope(Guid actor, Guid scopeId, IReadOnlySet<Guid> storeIds)
    {
        var old = data.Scopes.FirstOrDefault(s => s.Id == scopeId)?.StoreIds ?? new HashSet<Guid>();
        var touched = old.Except(storeIds).Union(storeIds.Except(old)).ToHashSet();
        return Permissions(actor).Privileges.TryGetValue(AccessPrivileges.ScopeUpdate, out var reach) && reach.CoversStores(touched)
            ? GuardResult.Allowed
            : GuardResult.Denied(GuardReason.NotInReach);
    }

    public GuardResult CanDeleteScope(Guid actor, Guid scopeId)
    {
        if (data.Assignments.Any(a => a.Reach.ScopeIds.Contains(scopeId))
            || data.Responsibilities.Any(r => r.Target is ResponsibilityTarget.Scope s && s.ScopeId == scopeId))
        {
            return GuardResult.Denied(GuardReason.InUse);
        }

        return Permissions(actor).Privileges.TryGetValue(AccessPrivileges.ScopeDelete, out var reach) && reach.Unrestricted
            ? GuardResult.Allowed
            : GuardResult.Denied(GuardReason.NoEscalation);
    }

    /// <summary>Setting or removing a responsibility requires reach over its target.</summary>
    public GuardResult CanSetResponsibility(Guid actor, ResponsibilityTarget target, string privilege = AccessPrivileges.ResponsibilityCreate)
    {
        var permissions = Permissions(actor);
        var allowed = target switch
        {
            ResponsibilityTarget.Employee e => permissions.AppliesToEmployee(privilege, e.EmployeeId),
            ResponsibilityTarget.Store s => permissions.AppliesToStore(privilege, s.StoreId),
            ResponsibilityTarget.Scope sc => _context.ScopesById.TryGetValue(sc.ScopeId, out var scope)
                && permissions.Privileges.TryGetValue(privilege, out var reach) && reach.CoversStores(scope.StoreIds),
            _ => false,
        };
        return allowed ? GuardResult.Allowed : GuardResult.Denied(GuardReason.NotInReach);
    }

    /// <summary>
    /// Setting or removing the main store of a person (a manual correction of the BC suggestion): reach over the person and, when a
    /// store is set, over that store too (nobody moves people into stores they do not reach).
    /// </summary>
    public GuardResult CanSetMainStore(Guid actor, Guid employeeId, Guid? storeId)
    {
        var permissions = Permissions(actor);
        return permissions.AppliesToEmployee(AccessPrivileges.MainStoreUpdate, employeeId)
            && (storeId is not { } store || permissions.AppliesToStore(AccessPrivileges.MainStoreUpdate, store))
            ? GuardResult.Allowed
            : GuardResult.Denied(GuardReason.NotInReach);
    }

    /// <summary>At least one active person with a valid, permanent, unrestricted assignment of a role containing <c>*</c>.</summary>
    public bool AdministratorsRemain(AclData candidate)
    {
        var administratorRoles = candidate.Roles.Where(r => r.Privileges.Contains(PrivilegePattern.Everything)).Select(r => r.Id).ToHashSet();
        return candidate.Assignments.Any(a =>
            administratorRoles.Contains(a.RoleId)
            && a.Reach.Unrestricted
            && a.ValidTo is null
            && a.IsValidAt(now)
            && candidate.People.FirstOrDefault(p => p.EmployeeId == a.EmployeeId)?.IsActiveAt(now, zone) == true);
    }

    private GuardResult RoleChange(Guid actor, string privilege, IReadOnlySet<string> before, IReadOnlySet<string> after, AclData resulting)
    {
        if (after.FirstOrDefault(p => !PrivilegePattern.IsKnown(p, _context.CatalogKeys)) is not null)
        {
            return GuardResult.Denied(GuardReason.UnknownPrivilege);
        }

        var actorPermissions = Permissions(actor);
        var touched = PrivilegePattern.Expand(before.Union(after), _context.CatalogKeys).Append(privilege);
        if (!Covers(actorPermissions, touched, requestedStores: null))
        {
            return GuardResult.Denied(GuardReason.NoEscalation);
        }

        return AdministratorsRemain(resulting) ? GuardResult.Allowed : GuardResult.Denied(GuardReason.LastAdministrator);
    }

    /// <summary>The actor has every key with a reach covering the requested stores (<c>null</c> = everywhere).</summary>
    private bool Covers(EffectivePermissions actor, IEnumerable<string> keys, IReadOnlySet<Guid>? requestedStores) =>
        keys.All(key => actor.Privileges.TryGetValue(key, out var reach)
                        && (requestedStores is null ? reach.Unrestricted : !_context.Scopable.Contains(key) || reach.CoversStores(requestedStores)));

    /// <summary>One of the actor's own assignments covering the key and reach lasts at least as long as requested.</summary>
    private bool OwnValidityCovers(Guid actor, string key, IReadOnlySet<Guid>? requestedStores, DateTimeOffset? requestedEnd)
    {
        var homeStore = Person(actor)?.HomeStoreId;
        return EffectivePermissionCalculator.EffectiveAssignments(actor, _context).Any(own =>
            _context.Grants(own.Role, own.Assignment.Reach, homeStore).Any(g =>
                g.Key == key
                && (g.Stores is null || (requestedStores is not null && (!_context.Scopable.Contains(key) || requestedStores.IsSubsetOf(g.Stores)))))
            && (own.Assignment.ValidTo is null || (requestedEnd is not null && requestedEnd <= own.Assignment.ValidTo)));
    }

    private EffectivePermissions Permissions(Guid employeeId) => EffectivePermissionCalculator.Calculate(employeeId, data, now, zone);

    private People.Person? Person(Guid employeeId) => data.People.FirstOrDefault(p => p.EmployeeId == employeeId);
}
