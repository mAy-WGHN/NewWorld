namespace Shared.Authorization;

/// <summary>Local copy of the permissions; every API checks against it and never calls the Access service per request.</summary>
public interface IPermissionCache
{
    /// <returns><c>true</c> if the snapshot was newer and has been applied.</returns>
    bool Apply(PermissionSnapshot snapshot);

    bool TryGet(Guid employeeId, out PermissionSnapshot? snapshot);

    /// <summary>A copy of all current snapshots (for services that need to know who holds a privilege, for example whom to notify).</summary>
    IReadOnlyList<PermissionSnapshot> All();

    /// <summary>Forgets a person (data erasure).</summary>
    void Remove(Guid employeeId);
}
