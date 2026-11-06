using Microsoft.EntityFrameworkCore;

namespace Shared.Infrastructure.Audit;

/// <summary>Personnel number and name of an actor.</summary>
public sealed record AuditPerson(string PersonnelNumber, string Name);

/// <summary>
/// Each service finds the actor in its own copy of the people (never by calling another service). Unknown people are logged with their id only.
/// The context is the one being saved; read with <c>AsNoTracking</c>.
/// </summary>
public interface IAuditPersonLookup
{
    Task<AuditPerson?> FindAsync(DbContext db, Guid employeeId, CancellationToken cancellationToken);
}
