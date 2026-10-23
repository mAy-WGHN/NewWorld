using Access.Contracts;
using Shared.Authorization;
using Shared.Infrastructure.Messaging;

namespace Portal.Infrastructure.Messaging;

/// <summary>Keeps this service's permission copy (table <c>permission_snapshots</c>) up to date; older versions are ignored.</summary>
public sealed class PermissionsChangedConsumer(PermissionsChangedHandler handler) : IMessageHandler<PermissionsChanged>
{
    public Task HandleAsync(PermissionsChanged message, CancellationToken cancellationToken) =>
        handler.HandleAsync(
            new PermissionSnapshot(
                message.EmployeeId,
                message.Version,
                message.Privileges.ToDictionary(
                    p => p.Key,
                    p => p.Unrestricted ? PrivilegeReach.Everywhere : PrivilegeReach.Stores(p.StoreIds, p.EmployeeIds),
                    StringComparer.Ordinal)),
            cancellationToken);
}
