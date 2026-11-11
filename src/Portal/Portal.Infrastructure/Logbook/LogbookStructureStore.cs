using Microsoft.EntityFrameworkCore;
using Portal.Core.Logbook;

namespace Portal.Infrastructure.Logbook;

/// <summary>
/// Reads and saves the hierarchy of the Berichtsheft. Saving takes the whole structure but only changes what differs (rows that stay are not touched,
/// so the audit log shows real changes only); what is gone is removed together with its leaders. The caller validates first
/// (<see cref="LogbookStructureValidator"/>) and checks people and stores against the copies.
/// </summary>
public sealed class LogbookStructureStore(PortalDbContext db)
{
    public async Task<LogbookStructure> LoadAsync(CancellationToken cancellationToken)
    {
        var districts = await db.LogbookDistricts.AsNoTracking().ToListAsync(cancellationToken);
        var districtLeaders = await db.LogbookDistrictLeaders.AsNoTracking().ToListAsync(cancellationToken);
        var regions = await db.LogbookRegions.AsNoTracking().ToListAsync(cancellationToken);
        var regionLeaders = await db.LogbookRegionLeaders.AsNoTracking().ToListAsync(cancellationToken);
        var stores = await db.LogbookStoreAssignments.AsNoTracking().ToListAsync(cancellationToken);

        return new LogbookStructure(
            [.. districts.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(d => new LogbookDistrict(d.Id, d.Name, [.. districtLeaders.Where(l => l.DistrictId == d.Id).Select(l => l.EmployeeId).Order()]))],
            [.. regions.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(r => new LogbookRegion(r.Id, r.DistrictId, r.Name, [.. regionLeaders.Where(l => l.RegionId == r.Id).Select(l => l.EmployeeId).Order()]))],
            [.. stores.OrderBy(s => s.StoreId).Select(s => new LogbookStoreAssignment(s.StoreId, s.DistrictId, s.RegionId))]);
    }

    public async Task SaveAsync(LogbookStructure structure, CancellationToken cancellationToken)
    {
        var districts = await db.LogbookDistricts.ToListAsync(cancellationToken);
        Sync(
            db.LogbookDistricts, districts, structure.Districts.Select(d => new LogbookDistrictRecord { Id = d.Id, Name = d.Name.Trim() }),
            d => d.Id, (existing, wanted) => existing.Name = wanted.Name);

        var districtLeaders = await db.LogbookDistrictLeaders.ToListAsync(cancellationToken);
        Sync(
            db.LogbookDistrictLeaders, districtLeaders,
            structure.Districts.SelectMany(d => d.LeaderIds.Distinct().Select(e => new LogbookDistrictLeaderRecord { DistrictId = d.Id, EmployeeId = e })),
            l => (l.DistrictId, l.EmployeeId), (_, _) => { });

        var regions = await db.LogbookRegions.ToListAsync(cancellationToken);
        Sync(
            db.LogbookRegions, regions, structure.Regions.Select(r => new LogbookRegionRecord { Id = r.Id, DistrictId = r.DistrictId, Name = r.Name.Trim() }),
            r => r.Id, (existing, wanted) =>
            {
                existing.Name = wanted.Name;
                existing.DistrictId = wanted.DistrictId;
            });

        var regionLeaders = await db.LogbookRegionLeaders.ToListAsync(cancellationToken);
        Sync(
            db.LogbookRegionLeaders, regionLeaders,
            structure.Regions.SelectMany(r => r.LeaderIds.Distinct().Select(e => new LogbookRegionLeaderRecord { RegionId = r.Id, EmployeeId = e })),
            l => (l.RegionId, l.EmployeeId), (_, _) => { });

        var stores = await db.LogbookStoreAssignments.ToListAsync(cancellationToken);
        Sync(
            db.LogbookStoreAssignments, stores,
            structure.Stores.Select(s => new LogbookStoreAssignmentRecord { StoreId = s.StoreId, DistrictId = s.DistrictId, RegionId = s.RegionId }),
            s => s.StoreId, (existing, wanted) =>
            {
                existing.DistrictId = wanted.DistrictId;
                existing.RegionId = wanted.RegionId;
            });

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Adds what is new, updates what stays (only properties that differ are marked as changed by EF) and removes what is gone.</summary>
    private static void Sync<TRecord, TKey>(
        DbSet<TRecord> set, List<TRecord> existing, IEnumerable<TRecord> wanted, Func<TRecord, TKey> key, Action<TRecord, TRecord> copy)
        where TRecord : class
        where TKey : notnull
    {
        var wantedByKey = wanted.GroupBy(key).ToDictionary(g => g.Key, g => g.First());
        var existingByKey = existing.ToDictionary(key);
        set.RemoveRange(existing.Where(e => !wantedByKey.ContainsKey(key(e))));
        foreach (var (k, record) in wantedByKey)
        {
            if (existingByKey.TryGetValue(k, out var current))
            {
                copy(current, record);
            }
            else
            {
                set.Add(record);
            }
        }
    }
}
