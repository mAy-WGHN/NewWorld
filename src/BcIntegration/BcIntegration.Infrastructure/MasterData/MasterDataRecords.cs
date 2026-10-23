namespace BcIntegration.Infrastructure.MasterData;

/// <summary>BC SystemId ↔ system-wide internal ID per entity type. The internal ID is issued here (UUIDv7).</summary>
public sealed class IdMapping
{
    public required string EntityType { get; set; }
    public Guid BcSystemId { get; set; }
    public Guid InternalId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Last published state of an employee (personal data; deleted on erasure).</summary>
public sealed class EmployeeStateRecord
{
    public Guid EmployeeId { get; set; }
    public required string PersonnelNumber { get; set; }
    public required string Name { get; set; }
    public Guid? HomeStoreId { get; set; }
    public string? Position { get; set; }
    public string? EmploymentType { get; set; }
    public decimal? ContractedHoursPerWeek { get; set; }
    public DateOnly? EntryDate { get; set; }
    public DateOnly? ExitDate { get; set; }
}

/// <summary>Erasure marker: the person's data was deleted and must not be copied from BC again.</summary>
public sealed class ErasedEmployee
{
    public Guid EmployeeId { get; set; }
    public DateTimeOffset ErasedAt { get; set; }
}

/// <summary>Last published state of a store.</summary>
public sealed class StoreStateRecord
{
    public Guid StoreId { get; set; }
    public required string StoreNumber { get; set; }
    public required string Name { get; set; }
    public string? Street { get; set; }
    public string? PostalCode { get; set; }
    public string? City { get; set; }

    /// <summary>Opening hours as JSON array.</summary>
    public required string OpeningHoursJson { get; set; }

    public bool IsActive { get; set; }
}

public sealed class SyncWatermarkRecord
{
    public required string EntityType { get; set; }
    public DateTimeOffset? LastModified { get; set; }
    public bool InitialLoadCompleted { get; set; }
}
