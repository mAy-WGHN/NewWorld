namespace Portal.Core.Logbook;

/// <summary>A district (Bezirk) with its district leaders (BL). A person may lead several districts.</summary>
public sealed record LogbookDistrict(Guid Id, string Name, IReadOnlyList<Guid> LeaderIds);

/// <summary>A region inside one district with its regional leaders (RL).</summary>
public sealed record LogbookRegion(Guid Id, Guid DistrictId, string Name, IReadOnlyList<Guid> LeaderIds);

/// <summary>
/// Where a store belongs: always to one district, and optionally to a region inside it. A store without a region is looked after by the district
/// leaders directly.
/// </summary>
public sealed record LogbookStoreAssignment(Guid StoreId, Guid DistrictId, Guid? RegionId);

/// <summary>The whole hierarchy of the Berichtsheft (decision 49): maintained in the Portal, not in Access.</summary>
public sealed record LogbookStructure(
    IReadOnlyList<LogbookDistrict> Districts, IReadOnlyList<LogbookRegion> Regions, IReadOnlyList<LogbookStoreAssignment> Stores);

/// <summary>The level at which somebody looks at a store: nothing, as regional leader or as district leader (the higher one counts).</summary>
public enum LogbookLevel
{
    None = 0,
    Regional = 1,
    District = 2,
}
