namespace Shared.Authorization;

public sealed class InMemoryPermissionCache : IPermissionCache
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, PermissionSnapshot> _byEmployee = [];
    private readonly Dictionary<string, Guid> _byExternalId = new(StringComparer.Ordinal);

    public bool Apply(PermissionSnapshot snapshot)
    {
        lock (_gate)
        {
            if (_byEmployee.TryGetValue(snapshot.EmployeeId, out var current))
            {
                if (snapshot.Version <= current.Version)
                {
                    return false;
                }

                if (current.ExternalId is not null)
                {
                    _byExternalId.Remove(current.ExternalId);
                }
            }

            _byEmployee[snapshot.EmployeeId] = snapshot;
            if (snapshot.ExternalId is not null)
            {
                _byExternalId[snapshot.ExternalId] = snapshot.EmployeeId;
            }

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

    public bool TryResolveEmployeeId(string externalId, out Guid employeeId)
    {
        lock (_gate)
        {
            return _byExternalId.TryGetValue(externalId, out employeeId);
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
