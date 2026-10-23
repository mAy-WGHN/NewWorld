namespace Access.Core.Acl;

/// <summary>Where a privilege applies: everywhere, or the stores and the employees whose home store is one of them.</summary>
public sealed record Reach(bool Unrestricted, IReadOnlySet<Guid> StoreIds, IReadOnlySet<Guid> EmployeeIds)
{
    public static readonly Reach Everywhere = new(true, new HashSet<Guid>(), new HashSet<Guid>());

    public bool CoversStores(IReadOnlySet<Guid>? stores) => Unrestricted || (stores is not null && stores.IsSubsetOf(StoreIds));
}

/// <summary>Effective privileges of a person at one moment.</summary>
/// <param name="Privileges">Concrete privileges with their reach.</param>
/// <param name="Roles">Names of the roles that take effect (including the public user role).</param>
/// <param name="StoreIds">Overview: all stores reached by restricted privileges.</param>
/// <param name="EmployeeIds">Overview: all employees reached, always including the person themself.</param>
public sealed record EffectivePermissions(
    IReadOnlyDictionary<string, Reach> Privileges,
    IReadOnlyList<string> Roles,
    IReadOnlySet<Guid> StoreIds,
    IReadOnlySet<Guid> EmployeeIds)
{
    public static EffectivePermissions None(Guid employeeId) =>
        new(new Dictionary<string, Reach>(), [], new HashSet<Guid>(), new HashSet<Guid> { employeeId });

    public bool AppliesToEmployee(string key, Guid employeeId) =>
        Privileges.TryGetValue(key, out var reach) && (reach.Unrestricted || reach.EmployeeIds.Contains(employeeId));

    public bool AppliesToStore(string key, Guid storeId) =>
        Privileges.TryGetValue(key, out var reach) && (reach.Unrestricted || reach.StoreIds.Contains(storeId));
}
