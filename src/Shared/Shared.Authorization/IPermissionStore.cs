namespace Shared.Authorization;

/// <summary>Persistence of the permission cache in the including service's own database.</summary>
public interface IPermissionStore
{
    Task<IReadOnlyList<PermissionSnapshot>> LoadAllAsync(CancellationToken cancellationToken);

    Task SaveAsync(PermissionSnapshot snapshot, CancellationToken cancellationToken);

    /// <summary>Deletes the stored copy of a person (data erasure).</summary>
    Task DeleteAsync(Guid employeeId, CancellationToken cancellationToken);
}
