namespace Shared.Authorization;

/// <summary>Local copy of the permissions; every API checks against it and never calls the Access service per request.</summary>
public interface IPermissionCache
{
    /// <returns><c>true</c> if the snapshot was newer and has been applied.</returns>
    bool Apply(PermissionSnapshot snapshot);

    bool TryGet(Guid employeeId, out PermissionSnapshot? snapshot);

    /// <summary>Forgets a person (data erasure).</summary>
    void Remove(Guid employeeId);
}
