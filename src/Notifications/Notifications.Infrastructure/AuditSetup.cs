using Microsoft.EntityFrameworkCore;
using Notifications.Infrastructure.Delivery;
using Shared.Infrastructure.Audit;

namespace Notifications.Infrastructure;

/// <summary>Audit of Notifications: the texts and settings people change; not the notifications themselves (in-app list, delivery records) and not the name copy.</summary>
public static class AuditExclusions
{
    public static void Configure(AuditOptions options) => options
        .Exclude<EmployeeNameRecord>()
        .Exclude<InAppNotificationRecord>()
        .Exclude<DeliveryRow>();
}

public sealed class NotificationsAuditPersonLookup : IAuditPersonLookup
{
    public async Task<AuditPerson?> FindAsync(DbContext db, Guid employeeId, CancellationToken cancellationToken)
    {
        var row = await db.Set<EmployeeNameRecord>().AsNoTracking().FirstOrDefaultAsync(r => r.EmployeeId == employeeId, cancellationToken);
        return row is null ? null : new AuditPerson(row.PersonnelNumber, $"{row.FirstName} {row.LastName}".Trim());
    }
}
