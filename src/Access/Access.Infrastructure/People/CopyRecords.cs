namespace Access.Infrastructure.People;

/// <summary>Read-only copy of a person (from BC Integration events).</summary>
public sealed class PersonRecord
{
    public Guid EmployeeId { get; set; }
    public required string PersonnelNumber { get; set; }
    public required string Name { get; set; }
    public Guid? HomeStoreId { get; set; }
    public string? Position { get; set; }
    public DateOnly? ExitDate { get; set; }
}

/// <summary>Read-only copy of a store (from BC Integration events).</summary>
public sealed class StoreRecord
{
    public Guid StoreId { get; set; }
    public required string StoreNumber { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; }
}
