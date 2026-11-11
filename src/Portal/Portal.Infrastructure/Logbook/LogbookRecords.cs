namespace Portal.Infrastructure.Logbook;

/// <summary>A district of the Berichtsheft (decision 49). The hierarchy is maintained here, not in Access.</summary>
public sealed class LogbookDistrictRecord
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
}

/// <summary>District leader (BL). The person is a copy (<c>people</c>); no foreign key, an erased person is removed by the erasure handler.</summary>
public sealed class LogbookDistrictLeaderRecord
{
    public Guid DistrictId { get; set; }
    public Guid EmployeeId { get; set; }
}

public sealed class LogbookRegionRecord
{
    public Guid Id { get; set; }
    public Guid DistrictId { get; set; }
    public required string Name { get; set; }
}

/// <summary>Regional leader (RL).</summary>
public sealed class LogbookRegionLeaderRecord
{
    public Guid RegionId { get; set; }
    public Guid EmployeeId { get; set; }
}

/// <summary>Where a store belongs: one district, optionally one region of it. The store is a copy (<c>stores</c>); no foreign key.</summary>
public sealed class LogbookStoreAssignmentRecord
{
    public Guid StoreId { get; set; }
    public Guid DistrictId { get; set; }
    public Guid? RegionId { get; set; }
}
