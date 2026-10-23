namespace BcIntegration.Core.MasterData;

/// <summary>
/// Employee as read from Business Central, already normalised by the BC client.
/// TODO(BC-Modell): source endpoint and fields (standard API v2.0 "employees" lacks store, position code, employment type, hours).
/// </summary>
public sealed record BcEmployee(
    Guid BcSystemId,
    string PersonnelNumber,
    string Name,
    string? StoreNumber,
    string? Position,
    string? EmploymentType,
    decimal? ContractedHoursPerWeek,
    DateOnly? EntryDate,
    DateOnly? ExitDate,
    DateTimeOffset LastModified);

/// <summary>Store as read from Business Central (LSC Store). TODO(BC-Modell): API page and fields.</summary>
public sealed record BcStore(
    Guid BcSystemId,
    string StoreNumber,
    string Name,
    Address? Address,
    IReadOnlyList<OpeningHours> OpeningHours,
    bool IsActive,
    DateTimeOffset LastModified);

public sealed record Address(string Street, string PostalCode, string City);

public sealed record OpeningHours(DayOfWeek Day, TimeOnly Opens, TimeOnly Closes);
