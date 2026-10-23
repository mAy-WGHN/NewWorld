using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shared.Authorization;

namespace Shared.Infrastructure.Authorization;

public sealed class EfPermissionStore<TContext>(TContext db) : IPermissionStore where TContext : DbContext
{
    public async Task<IReadOnlyList<PermissionSnapshot>> LoadAllAsync(CancellationToken cancellationToken)
    {
        var rows = await db.Set<PermissionSnapshotRecord>().AsNoTracking().ToListAsync(cancellationToken);
        return rows.Select(r => new PermissionSnapshot(r.EmployeeId, r.Version, Deserialize(r.PrivilegesJson))).ToList();
    }

    public async Task SaveAsync(PermissionSnapshot snapshot, CancellationToken cancellationToken)
    {
        var row = await db.Set<PermissionSnapshotRecord>().FindAsync([snapshot.EmployeeId], cancellationToken);
        if (row is null)
        {
            row = new PermissionSnapshotRecord { EmployeeId = snapshot.EmployeeId, PrivilegesJson = "{}" };
            db.Set<PermissionSnapshotRecord>().Add(row);
        }

        row.Version = snapshot.Version;
        row.PrivilegesJson = JsonSerializer.Serialize(snapshot.Privileges.ToDictionary(
            p => p.Key,
            p => new StoredReach(p.Value.Unrestricted, p.Value.StoreIds.ToList(), p.Value.EmployeeIds.ToList())));
        await db.SaveChangesAsync(cancellationToken);
    }

    private static Dictionary<string, PrivilegeReach> Deserialize(string json) =>
        (JsonSerializer.Deserialize<Dictionary<string, StoredReach>>(json) ?? [])
        .ToDictionary(p => p.Key, p => p.Value.Unrestricted ? PrivilegeReach.Everywhere : PrivilegeReach.Stores(p.Value.StoreIds, p.Value.EmployeeIds));

    private sealed record StoredReach(bool Unrestricted, List<Guid> StoreIds, List<Guid> EmployeeIds);
}
