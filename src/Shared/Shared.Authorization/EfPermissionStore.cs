using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Shared.Authorization;

public sealed class EfPermissionStore<TContext>(TContext db) : IPermissionStore where TContext : DbContext
{
    public async Task<IReadOnlyList<PermissionSnapshot>> LoadAllAsync(CancellationToken cancellationToken)
    {
        var rows = await db.Set<PermissionSnapshotRecord>().AsNoTracking().ToListAsync(cancellationToken);
        return rows.Select(r => new PermissionSnapshot(
            r.EmployeeId,
            r.Version,
            r.ExternalId,
            JsonSerializer.Deserialize<HashSet<Guid>>(r.StoreIdsJson) ?? [],
            JsonSerializer.Deserialize<HashSet<Guid>>(r.EmployeeIdsJson) ?? [])).ToList();
    }

    public async Task SaveAsync(PermissionSnapshot snapshot, CancellationToken cancellationToken)
    {
        var row = await db.Set<PermissionSnapshotRecord>().FindAsync([snapshot.EmployeeId], cancellationToken);
        if (row is null)
        {
            row = new PermissionSnapshotRecord { EmployeeId = snapshot.EmployeeId, StoreIdsJson = "[]", EmployeeIdsJson = "[]" };
            db.Set<PermissionSnapshotRecord>().Add(row);
        }

        row.Version = snapshot.Version;
        row.ExternalId = snapshot.ExternalId;
        row.StoreIdsJson = JsonSerializer.Serialize(snapshot.StoreIds);
        row.EmployeeIdsJson = JsonSerializer.Serialize(snapshot.EmployeeIds);
        await db.SaveChangesAsync(cancellationToken);
    }
}
