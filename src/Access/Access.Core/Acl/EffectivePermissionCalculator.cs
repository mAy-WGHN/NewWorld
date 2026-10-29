namespace Access.Core.Acl;

/// <summary>
/// Computes the effective privileges of a person (port of the Rollen-Rechte rules): union over all assignments valid at the
/// moment, plus the public user role for every active person. Wildcards expand to registered privileges only. A restricted
/// assignment reaches the stores of its scopes (and the home store, if chosen); non-scopable privileges take effect only in
/// unrestricted assignments. After the exit date nothing applies except the role for former employees (decision 36).
/// </summary>
public static class EffectivePermissionCalculator
{
    public static EffectivePermissions Calculate(Guid employeeId, AclData data, DateTimeOffset at, TimeZoneInfo zone)
    {
        var person = data.People.FirstOrDefault(p => p.EmployeeId == employeeId);
        if (person is null)
        {
            return EffectivePermissions.None(employeeId);
        }

        var exited = !person.IsActiveAt(at, zone);

        var context = new Context(data, at, zone);
        var unrestricted = new HashSet<string>(StringComparer.Ordinal);
        var stores = new Dictionary<string, HashSet<Guid>>(StringComparer.Ordinal);
        var roleNames = new List<string>();

        foreach (var (role, assignment) in exited ? FormerEmployeeAssignments(person.EmployeeId, context) : EffectiveAssignments(person.EmployeeId, context))
        {
            roleNames.Add(role.Name);
            foreach (var (key, reachedStores) in context.Grants(role, assignment.Reach, person.HomeStoreId))
            {
                if (reachedStores is null)
                {
                    unrestricted.Add(key);
                }
                else
                {
                    stores.TryAdd(key, []);
                    stores[key].UnionWith(reachedStores);
                }
            }
        }

        var privileges = new Dictionary<string, Reach>(StringComparer.Ordinal);
        foreach (var key in unrestricted)
        {
            privileges[key] = Reach.Everywhere;
        }

        foreach (var (key, keyStores) in stores.Where(s => !unrestricted.Contains(s.Key)))
        {
            privileges[key] = new Reach(false, keyStores, context.EmployeesOf(keyStores));
        }

        var overviewStores = privileges.Values.Where(r => !r.Unrestricted).SelectMany(r => r.StoreIds).ToHashSet();
        var overviewEmployees = privileges.Values.Where(r => !r.Unrestricted).SelectMany(r => r.EmployeeIds).Append(employeeId).ToHashSet();
        return new EffectivePermissions(privileges, roleNames.Distinct().ToList(), overviewStores, overviewEmployees);
    }

    /// <summary>After the exit only the role for former employees (unrestricted, so the own payroll statements and nothing else).</summary>
    private static IEnumerable<(Role Role, Assignment Assignment)> FormerEmployeeAssignments(Guid employeeId, Context context)
    {
        if (context.RolesById.TryGetValue(Role.FormerEmployeeId, out var former))
        {
            yield return (former, new Assignment(Guid.Empty, employeeId, Role.FormerEmployeeId, AssignmentReach.Everywhere, null, null, AssignmentSource.Unknown));
        }
    }

    /// <summary>
    /// The valid assignments of a person with their role, the public user role first.
    /// TODO(Entscheidung 13): further automatic assignments from home store + function (function only if the person has no or
    /// several linked stores) are to be specified; until then everything beyond the public user role is assigned manually.
    /// </summary>
    internal static IEnumerable<(Role Role, Assignment Assignment)> EffectiveAssignments(Guid employeeId, Context context)
    {
        if (context.RolesById.TryGetValue(Role.PublicUserId, out var publicUser))
        {
            yield return (publicUser, new Assignment(Guid.Empty, employeeId, Role.PublicUserId, AssignmentReach.Everywhere, null, null, AssignmentSource.Unknown));
        }

        foreach (var assignment in context.Data.Assignments.Where(a => a.EmployeeId == employeeId && a.IsValidAt(context.At)))
        {
            if (context.RolesById.TryGetValue(assignment.RoleId, out var role))
            {
                yield return (role, assignment);
            }
        }
    }

    internal sealed class Context
    {
        public Context(AclData data, DateTimeOffset at, TimeZoneInfo zone)
        {
            Data = data;
            At = at;
            RolesById = data.Roles.ToDictionary(r => r.Id);
            ScopesById = data.Scopes.ToDictionary(s => s.Id);
            CatalogKeys = data.Catalog.Select(c => c.Key).ToList();
            Scopable = data.Catalog.Where(c => c.Scopable).Select(c => c.Key).ToHashSet(StringComparer.Ordinal);
            EmployeesByStore = data.People
                .Where(p => p.HomeStoreId is not null && p.IsActiveAt(at, zone))
                .ToLookup(p => p.HomeStoreId!.Value, p => p.EmployeeId);
        }

        public AclData Data { get; }
        public DateTimeOffset At { get; }
        public IReadOnlyDictionary<Guid, Role> RolesById { get; }
        public IReadOnlyDictionary<Guid, ScopeDefinition> ScopesById { get; }
        public IReadOnlyList<string> CatalogKeys { get; }
        public IReadOnlySet<string> Scopable { get; }
        public ILookup<Guid, Guid> EmployeesByStore { get; }

        /// <summary>Stores of a restricted reach; <c>null</c> for unrestricted.</summary>
        public IReadOnlySet<Guid>? StoresOf(AssignmentReach reach, Guid? homeStoreId)
        {
            if (reach.Unrestricted)
            {
                return null;
            }

            var stores = reach.ScopeIds.Where(ScopesById.ContainsKey).SelectMany(id => ScopesById[id].StoreIds).ToHashSet();
            if (reach.IncludeHomeStore && homeStoreId is { } home)
            {
                stores.Add(home);
            }

            return stores;
        }

        /// <summary>Concrete privileges a role grants with a reach: <c>null</c> stores = everywhere. Ineffective grants are left out.</summary>
        public IEnumerable<(string Key, IReadOnlySet<Guid>? Stores)> Grants(Role role, AssignmentReach reach, Guid? homeStoreId)
        {
            var stores = StoresOf(reach, homeStoreId);
            foreach (var key in PrivilegePattern.Expand(role.Privileges, CatalogKeys))
            {
                if (stores is null)
                {
                    yield return (key, null);
                }
                else if (stores.Count > 0 && Scopable.Contains(key))
                {
                    yield return (key, stores);
                }
            }
        }

        public IReadOnlySet<Guid> EmployeesOf(IEnumerable<Guid> stores) => stores.SelectMany(s => EmployeesByStore[s]).ToHashSet();
    }
}
