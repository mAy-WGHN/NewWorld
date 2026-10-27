namespace Workforce.Core.Employees;

public enum Gender
{
    Unknown = 0,
    Female = 1,
    Male = 2,
    Diverse = 3,
}

/// <summary>The selection lists of the employee card, maintained by HR (entries are deactivated, never deleted).</summary>
public enum ReferenceKind
{
    Unknown = 0,
    Function = 1,
    Role = 2,
    Region = 3,
    Workplace = 4,
    EmploymentRelationship = 5,
    EmploymentCode = 6,
    AuthorizationGroup = 7,
    WageType = 8,
}

/// <summary>Where the card stands on its way to Business Central.</summary>
public enum CardState
{
    Unknown = 0,

    /// <summary>Created in this tool; BC does not know the person yet. Until BC takes the person over, the card is the source.</summary>
    PendingBc = 1,

    /// <summary>BC knows the person.</summary>
    Synced = 2,
}

/// <summary>An entry of a selection list.</summary>
public sealed class ReferenceEntry
{
    public Guid Id { get; set; }

    public ReferenceKind Kind { get; set; }

    /// <summary>Short code, unique within the list (case-insensitive).</summary>
    public required string Code { get; set; }

    public required string Name { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>
/// The consolidated employee card (replaces the two BC lists "Mitarbeiterliste (Personalplanung)" and "Kassiererübersicht"). Every piece of
/// information appears once. The personnel number is also the retail cashier ID. The password is never stored; only the flag
/// <see cref="PasswordChangeRequired"/>. Private data and the wage type are only shown to those who may see them (see the endpoints).
/// </summary>
public sealed class EmployeeCard
{
    public Guid EmployeeId { get; set; }

    /// <summary>Assigned from the number series when the card is created; never changes.</summary>
    public required string PersonnelNumber { get; set; }

    public required string FirstName { get; set; }

    public required string LastName { get; set; }

    /// <summary>Name on the receipt; usually the last name.</summary>
    public required string ReceiptName { get; set; }

    public Gender Gender { get; set; }

    public DateOnly? BirthDate { get; set; }

    // contact (private data)
    public string? Street { get; set; }
    public string? PostalCode { get; set; }
    public string? City { get; set; }
    public string? PrivatePhone { get; set; }
    public string? PrivateEmail { get; set; }

    public string? BusinessEmail { get; set; }

    // employment
    public Guid? MainStoreId { get; set; }
    public Guid? FunctionId { get; set; }
    public Guid? RoleId { get; set; }
    public Guid? RegionId { get; set; }
    public Guid? WorkplaceId { get; set; }
    public Guid? EmploymentRelationshipId { get; set; }
    public Guid? EmploymentCodeId { get; set; }
    public Guid? AuthorizationGroupId { get; set; }

    /// <summary>Wage type; visible for HR, administrators and the management only.</summary>
    public Guid? WageTypeId { get; set; }

    public DateOnly HireDate { get; set; }

    public DateOnly? ExitDate { get; set; }

    /// <summary>The supervisor (the card of the person with the personnel number entered).</summary>
    public Guid? SupervisorId { get; set; }

    public bool QtradoExport { get; set; }

    /// <summary>The time tracking tool needs this flag to allow clocking in; <c>true</c> by default.</summary>
    public bool WebTimeTrackingAllowed { get; set; } = true;

    public bool PasswordChangeRequired { get; set; } = true;

    /// <summary>Free text; no health information (data protection).</summary>
    public string? Note { get; set; }

    public CardState State { get; set; } = CardState.PendingBc;

    public DateTimeOffset CreatedAt { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTimeOffset ModifiedAt { get; set; }

    /// <summary>Active between hire and exit date (inclusive).</summary>
    public bool IsActiveOn(DateOnly day) => HireDate <= day && (ExitDate is not { } exit || day <= exit);

    public string FullName => FirstName + " " + LastName;
}

/// <summary>The data HR enters on the card (creation and change). The personnel number is assigned, never entered.</summary>
public sealed record EmployeeData(
    string FirstName,
    string LastName,
    string? ReceiptName,
    Gender Gender,
    DateOnly? BirthDate,
    string? Street,
    string? PostalCode,
    string? City,
    string? PrivatePhone,
    string? PrivateEmail,
    string? BusinessEmail,
    Guid? MainStoreId,
    Guid? FunctionId,
    Guid? RoleId,
    Guid? RegionId,
    Guid? WorkplaceId,
    Guid? EmploymentRelationshipId,
    Guid? EmploymentCodeId,
    Guid? AuthorizationGroupId,
    Guid? WageTypeId,
    DateOnly HireDate,
    DateOnly? ExitDate,
    string? SupervisorPersonnelNumber,
    bool QtradoExport,
    bool WebTimeTrackingAllowed = true,
    bool PasswordChangeRequired = true,
    string? Note = null);

public enum EmployeeError
{
    None = 0,

    /// <summary>A value is missing or not sensible (names, dates, formats).</summary>
    Invalid = 1,

    NotFound = 2,

    /// <summary>HR has not entered the last used personnel number yet; nobody can be created before.</summary>
    NumberSeriesNotSet = 3,

    DuplicateNumber = 4,

    /// <summary>An entry of a selection list does not exist, belongs to another list or is deactivated.</summary>
    UnknownReference = 5,

    UnknownStore = 6,

    UnknownSupervisor = 7,

    /// <summary>The code exists already in this list.</summary>
    DuplicateCode = 8,

    /// <summary>The caller lacks a right the change needs (e.g. the wage type).</summary>
    NotAllowed = 9,
}
