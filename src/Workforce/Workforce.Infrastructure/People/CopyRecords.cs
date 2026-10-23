namespace Workforce.Infrastructure.People;

/// <summary>Read-only copy of a person (from BC Integration events).</summary>
public sealed class PersonRecord
{
    public Guid EmployeeId { get; set; }
    public required string Name { get; set; }
    public Guid? HomeStoreId { get; set; }
    public DateOnly? ExitDate { get; set; }
}

/// <summary>Read-only copy of a store.</summary>
public sealed class StoreRecord
{
    public Guid StoreId { get; set; }
    public required string StoreNumber { get; set; }
    public required string Name { get; set; }
    /// <summary>City of the store address (municipal holidays); organisation data.</summary>
    public string? City { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>
/// Copy of Access's responsibilities for one person, reduced to what absences need: the persons who approve absences
/// (topic <see cref="Topic"/>). Version from <c>ResponsibilitiesChanged</c>; older versions are ignored.
/// </summary>
public sealed class ApproverCopyRecord
{
    public const string Topic = "absence.approval";

    public Guid EmployeeId { get; set; }
    public long Version { get; set; }
    public List<Guid> ApproverIds { get; set; } = [];
}
