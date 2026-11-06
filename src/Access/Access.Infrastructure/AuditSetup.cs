using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure.Audit;

namespace Access.Infrastructure;

/// <summary>Audit of Access: everything done through the API except copies, publication state and everything about sign-in and tokens (secrets, no actions of administration).</summary>
public static class AuditExclusions
{
    public static void Configure(AuditOptions options) => options
        .Exclude<People.PersonRecord>()
        .Exclude<People.StoreRecord>()
        .Exclude<Acl.CatalogEntryRecord>()
        .Exclude<Publishing.PublicationRecord>()
        .Exclude<Login.CredentialRecord>()
        .Exclude<Login.RefreshTokenRecord>()
        .Exclude<StepUp.StepUpRequestRecord>()
        .Exclude<StepUp.StepUpConfirmationRecord>();
}

public sealed class AccessAuditPersonLookup : IAuditPersonLookup
{
    public async Task<AuditPerson?> FindAsync(DbContext db, Guid employeeId, CancellationToken cancellationToken) =>
        await db.Set<People.PersonRecord>().AsNoTracking()
            .Where(p => p.EmployeeId == employeeId)
            .Select(p => new AuditPerson(p.PersonnelNumber, p.Name))
            .FirstOrDefaultAsync(cancellationToken);
}
