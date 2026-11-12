using Microsoft.EntityFrameworkCore;
using Portal.Infrastructure.Messaging;
using Shared.Infrastructure.Audit;

namespace Portal.Infrastructure;

/// <summary>Audit of the Portal: everything done through the API (including public filing of reports, as anonymous) except the copies from messages.</summary>
public static class AuditExclusions
{
    public static void Configure(AuditOptions options) => options
        .Exclude<PersonRecord>()
        .Exclude<StoreRecord>()
        .Exclude<MainStoreCopyRecord>()
        // the Berichtsheft is kept for one year (decision 49): its content must not live on in the log; who did what stays in the report itself
        .Exclude<Logbook.LogbookReportRecord>()
        .Exclude<Logbook.LogbookCommentRecord>();
}

public sealed class PortalAuditPersonLookup : IAuditPersonLookup
{
    public async Task<AuditPerson?> FindAsync(DbContext db, Guid employeeId, CancellationToken cancellationToken) =>
        await db.Set<PersonRecord>().AsNoTracking()
            .Where(p => p.EmployeeId == employeeId)
            .Select(p => new AuditPerson(p.PersonnelNumber, p.Name))
            .FirstOrDefaultAsync(cancellationToken);
}
