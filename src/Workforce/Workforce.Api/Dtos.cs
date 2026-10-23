namespace Workforce.Api;

public sealed record ReportSickRequest(DateOnly From, DateOnly? To, bool CertificateOnFile);

public sealed record RequestVacationRequest(DateOnly From, DateOnly To);

public sealed record EndSickRequest(DateOnly To);

public sealed record SetEntitlementRequest(decimal Days);

/// <summary>
/// An absence as its viewer may see it: <see cref="Type"/> and <see cref="CertificateOnFile"/> only for those with the reason privilege
/// (the person, supervisors, HR), <see cref="SyncStatus"/> only for those who may see the BC transfer.
/// </summary>
public sealed record AbsenceDto(
    Guid Id, Guid EmployeeId, string? Type, string Status, DateOnly From, DateOnly? To, bool? CertificateOnFile, long? Version, string? SyncStatus);

/// <summary>What colleagues in a store see: that somebody is absent. Name and period; type and status only with the reason privilege.</summary>
public sealed record StoreAbsenceDto(Guid EmployeeId, string EmployeeName, DateOnly From, DateOnly? To, string Status, string? Type);

public sealed record PendingApprovalDto(Guid AbsenceId, Guid EmployeeId, string EmployeeName, DateOnly From, DateOnly To);

public sealed record VacationBalanceDto(int Year, decimal Entitlement, decimal Taken, decimal Planned, decimal Remaining);

/// <summary>Everything about an absence for authorised services (App Role <c>Workforce.AbsenceDetails.Read</c>), including the type and the BC transfer status.</summary>
public sealed record AbsenceDetailsDto(
    Guid Id, Guid EmployeeId, string Type, string Status, DateOnly From, DateOnly? To, bool? CertificateOnFile, long Version, string SyncStatus, long? SyncedVersion);
