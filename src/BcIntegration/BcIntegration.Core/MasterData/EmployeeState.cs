namespace BcIntegration.Core.MasterData;

/// <summary>Last published state of an employee; compared to detect real changes.</summary>
public sealed record EmployeeState(
    string PersonnelNumber,
    string Name,
    Guid? HomeStoreId,
    string? Position,
    string? EmploymentType,
    decimal? ContractedHoursPerWeek,
    DateOnly? EntryDate,
    DateOnly? ExitDate);

/// <summary>Last published state of a store. Equality includes the opening hours by content.</summary>
public sealed record StoreState(
    string StoreNumber,
    string Name,
    Address? Address,
    IReadOnlyList<OpeningHours> OpeningHours,
    bool IsActive);
