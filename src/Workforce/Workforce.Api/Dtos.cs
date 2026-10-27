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
