namespace Shared.Authorization;

public sealed class InMemoryPermissionCache : IPermissionCache
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, PermissionSnapshot> _byEmployee = [];

    public bool Apply(PermissionSnapshot snapshot)
    {
        lock (_gate)
        {
            if (_byEmployee.TryGetValue(snapshot.EmployeeId, out var current) && snapshot.Version <= current.Version)
            {
                return false;
            }

            _byEmployee[snapshot.EmployeeId] = snapshot;
            return true;
        }
    }

    public bool TryGet(Guid employeeId, out PermissionSnapshot? snapshot)
    {
        lock (_gate)
        {
            return _byEmployee.TryGetValue(employeeId, out snapshot);
        }
    }

    public void Remove(Guid employeeId)
    {
        lock (_gate)
        {
            _byEmployee.Remove(employeeId);
        }
    }

    /// <summary>Restores the cache from the service's database after a start (last known state).</summary>
    public async Task LoadAsync(IPermissionStore store, CancellationToken cancellationToken = default)
    {
        foreach (var snapshot in await store.LoadAllAsync(cancellationToken))
        {
            Apply(snapshot);
        }
    }
}
