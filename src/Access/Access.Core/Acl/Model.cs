using Access.Core.People;
using Access.Core.Responsibilities;

namespace Access.Core.Acl;

/// <summary>A role: a named set of privilege patterns ("what").</summary>
public sealed record Role(Guid Id, string Name, IReadOnlySet<string> Privileges, bool BuiltIn)
{
    public static readonly Guid AdministratorId = Guid.Parse("a0000000-0000-0000-0000-000000000001");
    public static readonly Guid PublicUserId = Guid.Parse("a0000000-0000-0000-0000-000000000002");
    public const string AdministratorName = "Administrator";
    public const string PublicUserName = "Public User";

    /// <summary>Built in: all privileges; cannot be changed or deleted.</summary>
    public static readonly Role Administrator = new(AdministratorId, AdministratorName, new HashSet<string> { PrivilegePattern.Everything }, true);

    /// <summary>
    /// Built in: what everybody has. Given automatically and unrestricted to every active person (decision 13), so it should hold
    /// only harmless privileges; its privileges can be edited, the role cannot be deleted.
    /// </summary>
    public static readonly Role PublicUser = new(PublicUserId, PublicUserName, new HashSet<string>(), true);
}

/// <summary>A named set of stores ("where"): definitions per store, region or district.</summary>
public sealed record ScopeDefinition(Guid Id, string Name, IReadOnlySet<Guid> StoreIds);

/// <summary>Reach of an assignment: unrestricted, or the stores of its scopes and optionally the person's home store.</summary>
/// <remarks>A restricted reach without stores reaches nowhere (fail closed): removing members never widens access.</remarks>
public sealed record AssignmentReach(bool Unrestricted, IReadOnlySet<Guid> ScopeIds, bool IncludeHomeStore)
{
    public static readonly AssignmentReach Everywhere = new(true, new HashSet<Guid>(), false);

    public static AssignmentReach Scopes(IEnumerable<Guid> scopeIds, bool includeHomeStore = false) =>
        new(false, scopeIds.ToHashSet(), includeHomeStore);
}

public enum AssignmentSource
{
    Unknown = 0,
    Manual = 1,

    /// <summary>Configured first administrators.</summary>
    Bootstrap = 2,
}

/// <summary>A role given to a person, with reach and optional validity (e.g. a substitution). ValidTo is exclusive.</summary>
public sealed record Assignment(
    Guid Id,
    Guid EmployeeId,
    Guid RoleId,
    AssignmentReach Reach,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidTo,
    AssignmentSource Source)
{
    public bool IsValidAt(DateTimeOffset at) => (ValidFrom is null || ValidFrom <= at) && (ValidTo is null || at < ValidTo);

    public static Assignment Unrestricted(Guid employeeId, Guid roleId, DateTimeOffset? validFrom = null, DateTimeOffset? validTo = null) =>
        new(Guid.NewGuid(), employeeId, roleId, AssignmentReach.Everywhere, validFrom, validTo, AssignmentSource.Manual);

    public static Assignment Restricted(
        Guid employeeId,
        Guid roleId,
        bool includeHomeStore,
        IEnumerable<Guid>? scopeIds = null,
        DateTimeOffset? validFrom = null,
        DateTimeOffset? validTo = null) =>
        new(Guid.NewGuid(), employeeId, roleId, AssignmentReach.Scopes(scopeIds ?? [], includeHomeStore), validFrom, validTo, AssignmentSource.Manual);
}

/// <summary>Everything the calculations need, loaded once per run.</summary>
public sealed record AclData(
    IReadOnlyList<Person> People,
    IReadOnlyList<CatalogEntry> Catalog,
    IReadOnlyList<Role> Roles,
    IReadOnlyList<ScopeDefinition> Scopes,
    IReadOnlyList<Assignment> Assignments,
    IReadOnlyList<Responsibility> Responsibilities);
