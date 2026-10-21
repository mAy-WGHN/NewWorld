namespace Shared.Authorization;

/// <summary>
/// Applies a <c>PermissionsChanged</c> snapshot to the local cache and persists it. The message itself
/// (Access.Contracts, phase 2) is mapped to <see cref="PermissionSnapshot"/> by the consuming API.
/// </summary>
public sealed class PermissionsChangedHandler(IPermissionCache cache, IPermissionStore store)
{
    public async Task HandleAsync(PermissionSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        if (cache.Apply(snapshot))
        {
            await store.SaveAsync(snapshot, cancellationToken);
        }
    }
}
