using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure.Audit;

namespace Workforce.Infrastructure;

/// <summary>Audit of Workforce: everything done through the API except copies from messages, system bookkeeping and the access log of certificates (itself a log).</summary>
public static class AuditExclusions
{
    public static void Configure(AuditOptions options) => options
        .Exclude<People.PersonRecord>()
        .Exclude<People.StoreRecord>()
        .Exclude<People.ApproverCopyRecord>()
        .Exclude<People.MainStoreCopyRecord>()
        .Exclude<WorkingTime.PayoutCheckRecord>()
        .Exclude<Workforce.Core.Certificates.CertificateAccessEntry>();
}

public sealed class WorkforceAuditPersonLookup : IAuditPersonLookup
{
    public async Task<AuditPerson?> FindAsync(DbContext db, Guid employeeId, CancellationToken cancellationToken) =>
        await db.Set<People.PersonRecord>().AsNoTracking()
            .Where(p => p.EmployeeId == employeeId)
            .Select(p => new AuditPerson(p.PersonnelNumber ?? string.Empty, p.Name))
            .FirstOrDefaultAsync(cancellationToken);
}
