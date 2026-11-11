namespace Portal.Core.Logbook;

/// <summary>Whom a report of a store goes to (regional leaders) and whom it is escalated to (district leaders).</summary>
public sealed record LogbookRecipients(IReadOnlyList<Guid> Regional, IReadOnlyList<Guid> District)
{
    /// <summary>The ones to tell first: the regional leaders, and if the store has none (no region, or a region without leaders) the district leaders.</summary>
    public IReadOnlyList<Guid> Primary => Regional.Count > 0 ? Regional : District;
}

/// <summary>
/// Who reaches which store (the "where" for regional and district leaders; the "what" comes from the privileges). A regional leader reaches the stores
/// of the own regions, a district leader everything in the own districts: all regions and the stores that belong to the district directly. Somebody who
/// leads nothing reaches nothing.
/// </summary>
public sealed record LogbookReach(IReadOnlySet<Guid> StoreIds, bool IsRegionalLeader, bool IsDistrictLeader)
{
    public LogbookLevel Level => IsDistrictLeader ? LogbookLevel.District : IsRegionalLeader ? LogbookLevel.Regional : LogbookLevel.None;

    public static LogbookReach Of(LogbookStructure structure, Guid person)
    {
        var districts = structure.Districts.Where(d => d.LeaderIds.Contains(person)).Select(d => d.Id).ToHashSet();
        var regions = structure.Regions.Where(r => r.LeaderIds.Contains(person)).Select(r => r.Id).ToHashSet();
        var stores = structure.Stores
            .Where(s => districts.Contains(s.DistrictId) || (s.RegionId is { } region && regions.Contains(region)))
            .Select(s => s.StoreId)
            .ToHashSet();
        return new LogbookReach(stores, regions.Count > 0, districts.Count > 0);
    }

    /// <summary>The level at which the person looks at this store; unknown stores and stores outside the reach give <see cref="LogbookLevel.None"/>.</summary>
    public static LogbookLevel LevelFor(LogbookStructure structure, Guid person, Guid storeId)
    {
        if (structure.Stores.FirstOrDefault(s => s.StoreId == storeId) is not { } assignment)
        {
            return LogbookLevel.None;
        }

        if (structure.Districts.Any(d => d.Id == assignment.DistrictId && d.LeaderIds.Contains(person)))
        {
            return LogbookLevel.District;
        }

        return assignment.RegionId is { } regionId && structure.Regions.Any(r => r.Id == regionId && r.LeaderIds.Contains(person))
            ? LogbookLevel.Regional
            : LogbookLevel.None;
    }

    public static LogbookRecipients RecipientsOf(LogbookStructure structure, Guid storeId)
    {
        if (structure.Stores.FirstOrDefault(s => s.StoreId == storeId) is not { } assignment)
        {
            return new LogbookRecipients([], []);
        }

        IReadOnlyList<Guid> regional = assignment.RegionId is { } regionId
            ? [.. structure.Regions.Where(r => r.Id == regionId).SelectMany(r => r.LeaderIds).Distinct()]
            : [];
        IReadOnlyList<Guid> district = [.. structure.Districts.Where(d => d.Id == assignment.DistrictId).SelectMany(d => d.LeaderIds).Distinct()];
        return new LogbookRecipients(regional, district);
    }

    /// <summary>A report for the store can only be handed in if somebody is told (the store is in the structure and has a leader to go to).</summary>
    public static bool CanReceiveReports(LogbookStructure structure, Guid storeId) => RecipientsOf(structure, storeId).Primary.Count > 0;
}
