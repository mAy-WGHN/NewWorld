using Access.Core.Publishing;
using Microsoft.EntityFrameworkCore;

namespace Access.Infrastructure.Publishing;

public sealed class EfPublicationStore(AccessDbContext db) : IPublicationStore
{
    public async Task<IReadOnlyDictionary<(Guid EmployeeId, PublicationKind Kind), Publication>> LoadAllAsync(CancellationToken cancellationToken) =>
        (await db.Publications.AsNoTracking().ToListAsync(cancellationToken))
            .ToDictionary(p => (p.EmployeeId, Enum.Parse<PublicationKind>(p.Kind)), p => new Publication(p.Version, p.Fingerprint));

    public async Task SaveAsync(Guid employeeId, PublicationKind kind, Publication publication, CancellationToken cancellationToken)
    {
        var record = await db.Publications.FindAsync([employeeId, kind.ToString()], cancellationToken);
        if (record is null)
        {
            record = new PublicationRecord { EmployeeId = employeeId, Kind = kind.ToString(), Fingerprint = publication.Fingerprint };
            db.Publications.Add(record);
        }

        record.Version = publication.Version;
        record.Fingerprint = publication.Fingerprint;
    }
}
