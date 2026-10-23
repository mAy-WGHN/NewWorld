namespace Shared.Authorization;

/// <summary>Where a privilege applies.</summary>
public sealed record PrivilegeReach(bool Unrestricted, IReadOnlySet<Guid> StoreIds, IReadOnlySet<Guid> EmployeeIds)
{
    public static readonly PrivilegeReach Everywhere = new(true, new HashSet<Guid>(), new HashSet<Guid>());

    public static PrivilegeReach Stores(IEnumerable<Guid> storeIds, IEnumerable<Guid> employeeIds) =>
        new(false, storeIds.ToHashSet(), employeeIds.ToHashSet());
}

/// <summary>
/// Effective privileges of one person as published by the Access service (<c>PermissionsChanged</c>).
/// <paramref name="Version"/> rises strictly per person; older or equal versions are ignored. Keys are concrete
/// catalogue privileges (Access expands wildcards); matching is exact.
/// </summary>
public sealed record PermissionSnapshot(Guid EmployeeId, long Version, IReadOnlyDictionary<string, PrivilegeReach> Privileges)
{
    public bool Has(string key) => Privileges.ContainsKey(key);

    public bool AppliesToStore(string key, Guid storeId) =>
        Privileges.TryGetValue(key, out var reach) && (reach.Unrestricted || reach.StoreIds.Contains(storeId));

    public bool AppliesToEmployee(string key, Guid employeeId) =>
        Privileges.TryGetValue(key, out var reach) && (reach.Unrestricted || reach.EmployeeIds.Contains(employeeId));
}
