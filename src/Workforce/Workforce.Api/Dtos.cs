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

public sealed record VacationBalanceDto(int Year, decimal Entitlement, decimal Taken, decimal Planned, decimal Remaining);

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
