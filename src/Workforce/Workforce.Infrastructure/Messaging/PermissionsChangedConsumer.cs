using Access.Contracts;
using Shared.Authorization;
using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure.Messaging;
using Workforce.Infrastructure.People;

namespace Workforce.Infrastructure.Messaging;

/// <summary>
/// Keeps this service's permission copy (table <c>permission_snapshots</c>) up to date; older versions are ignored. Also takes over the
/// main store Access publishes: it is the one maintained in Access, otherwise BC's suggestion, and decides where a person counts.
/// </summary>
public sealed class PermissionsChangedConsumer(PermissionsChangedHandler handler, WorkforceDbContext db) : IMessageHandler<PermissionsChanged>
{
    public async Task HandleAsync(PermissionsChanged message, CancellationToken cancellationToken)
    {
        await TakeOverMainStoreAsync(message, cancellationToken);
        await handler.HandleAsync(
            new PermissionSnapshot(
                message.EmployeeId,
                message.Version,
                message.Privileges.ToDictionary(
                    p => p.Key,
                    p => p.Unrestricted ? PrivilegeReach.Everywhere : PrivilegeReach.Stores(p.StoreIds, p.EmployeeIds),
                    StringComparer.Ordinal)),
            cancellationToken);
    }

    private async Task TakeOverMainStoreAsync(PermissionsChanged message, CancellationToken cancellationToken)
    {
        var copy = await db.MainStoreCopies.FindAsync([message.EmployeeId], cancellationToken);
        if (copy is not null && message.Version <= copy.Version)
        {
            return;
        }

        if (copy is null)
        {
            copy = new MainStoreCopyRecord { EmployeeId = message.EmployeeId };
            db.MainStoreCopies.Add(copy);
        }

        copy.StoreId = message.MainStoreId;
        copy.Version = message.Version;
        if (await db.People.FindAsync([message.EmployeeId], cancellationToken) is { } person)
        {
            person.HomeStoreId = message.MainStoreId ?? person.BcHomeStoreId;
        }
    }
}
