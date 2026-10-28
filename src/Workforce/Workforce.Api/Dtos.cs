namespace Workforce.Api;

/// <summary>A sick note. The medical certificate (AU) is optional here and can be uploaded afterwards.</summary>
public sealed record ReportSickRequest(DateOnly From, DateOnly? To);

public sealed record RequestVacationRequest(DateOnly From, DateOnly To);

public sealed record EndSickRequest(DateOnly To);

public sealed record SetEntitlementRequest(decimal Days);

/// <summary>
/// An absence as its viewer may see it: <see cref="Type"/> only for those with the reason privilege (the person, supervisors, HR),
/// <see cref="CertificateStatus"/>/<see cref="CertifiedThrough"/> only for the person and holders of the certificate status privilege,
/// <see cref="SyncStatus"/> only for those who may see the BC transfer.
/// </summary>
public sealed record AbsenceDto(
    Guid Id, Guid EmployeeId, string? Type, string Status, DateOnly From, DateOnly? To, string? CertificateStatus, DateOnly? CertifiedThrough, long? Version, string? SyncStatus);

/// <summary>What colleagues in a store see: that somebody is absent. Name and period; type and status only with the reason privilege.</summary>
public sealed record StoreAbsenceDto(Guid EmployeeId, string EmployeeName, DateOnly From, DateOnly? To, string Status, string? Type);

public sealed record PendingApprovalDto(Guid AbsenceId, Guid EmployeeId, string EmployeeName, DateOnly From, DateOnly To);

/// <summary>
/// The vacation account of a year. <see cref="Entitlement"/> are the effective vacation days (contractual days, pro rata in the year of
/// joining, plus additional days that have not expired); the other fields show how they come about.
/// </summary>
public sealed record VacationBalanceDto(
    int Year, decimal Entitlement, decimal Taken, decimal Planned, decimal Remaining,
    decimal ContractualAnnualDays = 0, decimal ContractualDays = 0, decimal ExtraDays = 0, decimal ForfeitedExtraDays = 0,
    IReadOnlyList<ExtraDaysLineDto>? ExtraLines = null);

public sealed record ExtraDaysLineDto(Guid Id, string Kind, int Year, decimal Days, decimal AvailableInYear, decimal UsedInYear, decimal Forfeited, DateOnly? ExpiresOn);

/// <summary>Additional vacation days (leftover or special leave) of a person.</summary>
public sealed record ExtraVacationDto(Guid Id, string Kind, int Year, decimal Days, DateOnly UsableFrom, DateOnly? ExpiresOn);

/// <summary>
/// The vacation rules HR maintains for everybody. Expiry of additional days: month and day empty = they never expire (default).
/// Waiting period: months after the entry (0 = none) and mode <c>Accrued</c> (only days earned so far) or <c>Blocked</c> (no vacation).
/// Rounding of pro rata days: <c>Up</c> (default), <c>Nearest</c>, <c>Down</c>, <c>None</c>, in steps of 0.5 or 1 day.
/// </summary>
public sealed record VacationRulesDto(
    int? ExtraDaysExpiryMonth, int? ExtraDaysExpiryDay, int WaitingPeriodMonths, string WaitingPeriodMode, string ProRataRounding, decimal ProRataRoundingStep,
    bool IsDefault, DateTimeOffset? ChangedAt, Guid? ChangedBy);

/// <summary>Replaces all rules; what is left out gets the default (nothing expires, no waiting period, rounding up to half days).</summary>
public sealed record SetVacationRulesRequest(
    int? ExtraDaysExpiryMonth, int? ExtraDaysExpiryDay, int? WaitingPeriodMonths, string? WaitingPeriodMode, string? ProRataRounding, decimal? ProRataRoundingStep);

/// <summary>A change of the annual vacation entitlement during a year (e.g. other working days per week); applies from the month after the date, or from the date if it is the first of a month.</summary>
public sealed record EntitlementChangeDto(Guid Id, DateOnly EffectiveFrom, decimal AnnualDays);

public sealed record AddEntitlementChangeRequest(DateOnly EffectiveFrom, decimal AnnualDays);

public sealed record AddExtraVacationRequest(string Kind, int Year, decimal Days);

/// <summary>Everything about an absence for authorised services (service role <c>Workforce.AbsenceDetails.Read</c>), including the type and the BC transfer status.</summary>
public sealed record AbsenceDetailsDto(
    Guid Id, Guid EmployeeId, string Type, string Status, DateOnly From, DateOnly? To, string CertificateStatus, long Version, string SyncStatus, long? SyncedVersion);

/// <summary>
/// Metadata of an uploaded certificate: enough to see that one is there and where it stands. Never the content and never a file name.
/// <see cref="UploadedBy"/> only for the uploader and reviewers; <see cref="CanOpen"/> says whether the caller may open the file.
/// </summary>
public sealed record CertificateDto(
    Guid Id, Guid AbsenceId, string State, DateOnly CertifiedFrom, DateOnly CertifiedTo, DateTimeOffset UploadedAt, Guid? UploadedBy,
    string ContentType, long Length, string? RejectionReason, bool HasContent, bool CanOpen);

public sealed record RejectCertificateRequest(string Reason);

/// <summary>How long after the end of a sick note the files are kept; <c>IsDefault</c> while HR has not set a value.</summary>
public sealed record CertificateRetentionDto(int RetentionAfterEndDays, bool IsDefault, DateTimeOffset? ChangedAt, Guid? ChangedBy);

public sealed record SetCertificateRetentionRequest(int RetentionAfterEndDays);

public sealed record StoreHolidayOpeningDto(Guid StoreId, bool OpensOnHolidays, DateTimeOffset? ChangedAt, Guid? ChangedBy);

public sealed record SetStoreHolidayOpeningRequest(bool OpensOnHolidays);

/// <summary>Sets the holiday opening of many stores at once (e.g. all railway-station stores); all or none.</summary>
public sealed record SetStoresHolidayOpeningRequest(IReadOnlyList<Guid> StoreIds, bool OpensOnHolidays);

public sealed record CertificateReminderSettingsDto(int FirstReminderAfterReturnDays, int ReminderEveryDays, int MaxReminders, bool IsDefault, DateTimeOffset? ChangedAt, Guid? ChangedBy);

public sealed record SetCertificateReminderSettingsRequest(int FirstReminderAfterReturnDays, int ReminderEveryDays, int MaxReminders);

/// <summary>One sick note in the HR overview: whether the certificate is missing, submitted, verified or rejected.</summary>
public sealed record SickNoteOverviewDto(
    Guid AbsenceId, Guid EmployeeId, string EmployeeName, Guid? StoreId, DateOnly From, DateOnly? To, string CertificateStatus,
    DateOnly? CertifiedThrough, int RemindersSent, DateTimeOffset? LastReminderAt);

public sealed record PageDto<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

/// <summary>An entry of the certificate access log: who did what to which document, when.</summary>
public sealed record CertificateAccessEntryDto(
    Guid Id, DateTimeOffset At, Guid? ActorId, string Action, Guid AbsenceId, Guid DocumentId, Guid EmployeeId);

// ---------------------------------------------------------------- employee card

/// <summary>
/// The data HR enters on the card (creating and changing). The personnel number is assigned, the password is never part of the card.
/// <see cref="Gender"/> is `Female`, `Male` or `Diverse`. Selections (function, role, ...) are ids of entries of the selection lists.
/// </summary>
public sealed record EmployeeRequest(
    string FirstName,
    string LastName,
    string? ReceiptName,
    string Gender,
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
    string? Note = null,
    decimal? VacationDaysPerYear = null)
{
    public Workforce.Core.Employees.EmployeeData ToData(Workforce.Core.Employees.Gender gender) => new(
        FirstName, LastName, ReceiptName, gender, BirthDate, Street, PostalCode, City, PrivatePhone, PrivateEmail, BusinessEmail, MainStoreId, FunctionId, RoleId, RegionId,
        WorkplaceId, EmploymentRelationshipId, EmploymentCodeId, AuthorizationGroupId, WageTypeId, HireDate, ExitDate, SupervisorPersonnelNumber, QtradoExport,
        WebTimeTrackingAllowed, PasswordChangeRequired, Note);
}

public sealed record CreatedEmployeeDto(Guid EmployeeId, string PersonnelNumber);

/// <summary>
/// The card as its viewer may see it: <see cref="Private"/> only for those with the private-data privilege and the person themself,
/// <see cref="Wage"/> only for those with the wage type privilege (never the person).
/// </summary>
public sealed record EmployeeCardDto(
    Guid EmployeeId, string PersonnelNumber, string FirstName, string LastName, string ReceiptName, string? BusinessEmail, Guid? MainStoreId, Guid? FunctionId, Guid? RoleId,
    Guid? RegionId, Guid? WorkplaceId, Guid? EmploymentRelationshipId, Guid? EmploymentCodeId, Guid? AuthorizationGroupId, DateOnly HireDate, DateOnly? ExitDate,
    Guid? SupervisorId, bool QtradoExport, bool WebTimeTrackingAllowed, bool PasswordChangeRequired, string? Note, string State,
    EmployeePrivateDto? Private, EmployeeWageDto? Wage);

public sealed record EmployeePrivateDto(string Gender, DateOnly? BirthDate, string? Street, string? PostalCode, string? City, string? PrivatePhone, string? PrivateEmail);

public sealed record EmployeeWageDto(Guid? WageTypeId);

/// <summary>The number series of the personnel numbers: last number given out, range and how many numbers are left; all empty until HR has entered the last number.</summary>
public sealed record NumberSeriesDto(long? LastNumber, long? StartNumber = null, long? EndNumber = null, long? Remaining = null);

/// <summary>The last number given out so far and, optionally, the range (kept if left out; at the first entry 1001 to 99999).</summary>
public sealed record SetNumberSeriesRequest(long LastNumber, long? StartNumber = null, long? EndNumber = null);

public sealed record ReferenceEntryDto(Guid Id, string Kind, string Code, string Name, bool IsActive);

public sealed record AddReferenceEntryRequest(string Code, string Name);

public sealed record UpdateReferenceEntryRequest(string Name, bool IsActive);

// ---------------------------------------------------------------- change requests

/// <summary>What a person asks for: contact fields to change (left out = unchanged) and/or a message. The person cannot change anything themself.</summary>
public sealed record ChangeRequestBody(string? Street, string? PostalCode, string? City, string? PrivatePhone, string? PrivateEmail, string? Message);

public sealed record ChangeFieldsDto(string? Street, string? PostalCode, string? City, string? PrivatePhone, string? PrivateEmail);

/// <summary>A change request. <see cref="PersonnelNumber"/> and <see cref="PersonName"/> are filled for those who decide, not in the person's own list.</summary>
public sealed record EmployeeChangeRequestDto(
    Guid Id, Guid EmployeeId, string? PersonnelNumber, string? PersonName, string Status, ChangeFieldsDto Fields, string? Message,
    DateTimeOffset CreatedAt, DateTimeOffset? DecidedAt, string? RejectionReason);

public sealed record RejectChangeRequestBody(string? Reason);

/// <summary>A kind of unavailability (BC list "Nichtverfügbarkeitsarten").</summary>
public sealed record UnavailabilityKindDto(Guid Id, string Code, string Description, bool PaidTime, string? ScheduleSymbol, string? WorkCode, bool IsActive, bool IsSystem);

public sealed record AddUnavailabilityKindRequest(string Code, string Description, bool PaidTime, string? ScheduleSymbol, string? WorkCode);

/// <summary>Everything but the code. <see cref="IsActive"/> left out keeps the state.</summary>
public sealed record UpdateUnavailabilityKindRequest(string Description, bool PaidTime, string? ScheduleSymbol, string? WorkCode, bool? IsActive);

public sealed record AddUnavailabilityRequest(Guid KindId, DateOnly From, DateOnly To);

/// <summary>
/// A period in which a person is not available, as its viewer may see it: the kind (<see cref="Kind"/> and the fields after it) only for the person
/// themself and holders of the reason privilege (HR, supervisors); colleagues with the availability privilege see the kind for sickness, vacation and
/// KU (what the schedule shows anyway) and otherwise only <c>Status</c> = <c>Unavailable</c> and the period (no <see cref="Id"/>, <see cref="PaidTime"/>, <see cref="Origin"/>). <see cref="Origin"/> is <c>Absence</c>, <c>Manual</c> or <c>System</c>.
/// </summary>
public sealed record UnavailabilityDto(
    Guid EmployeeId, DateOnly From, DateOnly? To, string Status, Guid? Id = null, string? Kind = null, string? Description = null, string? ScheduleSymbol = null, bool? PaidTime = null, string? Origin = null);

/// <summary>What colleagues in a store see (name and period); the kind only with the reason privilege.</summary>
public sealed record StoreUnavailabilityDto(string EmployeeName, UnavailabilityDto Period);

public sealed record BookHoursRequest(decimal Hours, DateOnly Date);

public sealed record CloseMonthRequest(int Year, int Month, decimal WorkedHours);

/// <summary>Both left out = the defaults: payout at any balance, nothing expires.</summary>
public sealed record SetWorkingTimeRulesRequest(decimal? PayoutThresholdHours, int? ExpiryMonths);

public sealed record WorkingTimeRulesDto(decimal PayoutThresholdHours, int? ExpiryMonths, bool IsDefault, DateTimeOffset? ChangedAt, Guid? ChangedBy);

public sealed record SetThresholdRequest(decimal? Hours);

public sealed record MonthlyThresholdDto(Guid RelationshipId, decimal Hours);

public sealed record TimeBookingDto(Guid Id, string Kind, decimal Hours, DateOnly Date, Guid? MonthCloseId, DateTimeOffset CreatedAt);

public sealed record MonthCloseDto(Guid Id, int Year, int Month, decimal WorkedHours, decimal ThresholdHours, decimal OvertimeHours, DateTimeOffset ClosedAt);

public sealed record AccountBucketDto(DateOnly EarnedOn, decimal Remaining, DateOnly? ExpiresOn);

/// <summary>
/// The working time account of a person: <c>Balance</c> today (hours still there minus <c>Deficit</c>), what expired (<c>Forfeited</c>), the hours still there
/// by earning date, the bookings (newest first) and the month closes. Class 2 data: only the person themself and holders of the read privilege.
/// </summary>
public sealed record WorkingTimeDto(
    decimal Balance, decimal Deficit, decimal Forfeited, IReadOnlyList<AccountBucketDto> Buckets, IReadOnlyList<TimeBookingDto> Bookings, IReadOnlyList<MonthCloseDto> Closes, WorkingTimeRulesDto Rules);

public sealed record MonthCloseResultDto(MonthCloseDto Close, TimeBookingDto? Booking);
