namespace Workforce.Infrastructure.People;

/// <summary>Read-only copy of a person (from BC Integration events).</summary>
public sealed class PersonRecord
{
    public Guid EmployeeId { get; set; }
    public required string Name { get; set; }
    /// <summary>The effective main store: the one from Access (<see cref="MainStoreCopyRecord"/>) if it has one, otherwise <see cref="BcHomeStoreId"/>.</summary>
    public Guid? HomeStoreId { get; set; }
    /// <summary>The suggestion from BC (the "Vorgabefiliale"), from <c>EmployeeChanged</c>.</summary>
    public Guid? BcHomeStoreId { get; set; }
    public DateOnly? ExitDate { get; set; }
}

/// <summary>
/// The main store of a person as Access publishes it (the one maintained there, otherwise BC's suggestion), from <c>PermissionsChanged</c>.
/// Kept separately so it survives arriving before the person's copy; older versions are ignored.
/// </summary>
public sealed class MainStoreCopyRecord
{
    public Guid EmployeeId { get; set; }
    public Guid? StoreId { get; set; }
    public long Version { get; set; }
}

/// <summary>
/// Setting of a store maintained in Workforce (not from BC): whether it is open on public holidays. No row = closed on holidays.
/// </summary>
public sealed class StoreSettingsRecord
{
    public Guid StoreId { get; set; }
    public bool OpensOnHolidays { get; set; }
    public Guid ChangedBy { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
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
