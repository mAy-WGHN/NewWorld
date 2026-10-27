using Shared.Approvals;
using Shared.Messaging;

namespace Workforce.Core.Absences;

/// <summary>Read-only copy of a person (from BC Integration events).</summary>
/// <param name="EmployeeId">Internal id.</param>
/// <param name="Name">Display name.</param>
/// <param name="HomeStoreId">Home store.</param>
/// <param name="ExitDate">Exit date, if any.</param>
/// <param name="StoreCity">City of the home store (for municipal holidays); <c>null</c> if unknown.</param>
/// <param name="StoreOpensOnHolidays">The home store is open on public holidays, so they are working days for its staff.</param>
/// <param name="EntryDate">Date of joining (from BC); <c>null</c> if unknown. In the year of joining the vacation days are pro rata.</param>
public sealed record PersonInfo(Guid EmployeeId, string Name, Guid? HomeStoreId, DateOnly? ExitDate, string? StoreCity = null, bool StoreOpensOnHolidays = false, DateOnly? EntryDate = null);

public interface IPeopleDirectory
{
    Task<PersonInfo?> GetAsync(Guid employeeId, CancellationToken cancellationToken);
}

/// <summary>Absences and approvals of the service. Changes are saved by the caller (one unit of work with the outbox).</summary>
public interface IAbsenceStore
{
    /// <summary>Serialises changes per person (two parallel requests must not both pass the overlap or balance check).</summary>
    Task LockEmployeeAsync(Guid employeeId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Absence>> ForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken);

    Task<Absence?> GetAsync(Guid absenceId, CancellationToken cancellationToken);

    void Add(Absence absence);

    Task<ApprovalRequest?> GetApprovalAsync(Guid approvalId, CancellationToken cancellationToken);

    void AddApproval(ApprovalRequest approval);
}

/// <summary>Vacation entitlement per person and year (maintained manually; TODO(BC-Modell): later import).</summary>
public interface IEntitlementStore
{
    Task<decimal?> GetAsync(Guid employeeId, int year, CancellationToken cancellationToken);

    Task SetAsync(Guid employeeId, int year, decimal days, CancellationToken cancellationToken);
}

/// <summary>Additional vacation days per person (leftover, special leave), entered by HR.</summary>
public interface IExtraVacationStore
{
    Task<IReadOnlyList<ExtraVacationDays>> ForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken);

    void Add(Guid employeeId, ExtraVacationDays extra, Guid createdBy, DateTimeOffset at);

    /// <returns><c>false</c> if the person has no such entry.</returns>
    Task<bool> RemoveAsync(Guid employeeId, Guid id, CancellationToken cancellationToken);
}

/// <summary>The persons responsible for a person for absences (topic <c>absence.approval</c>, copy of Access's responsibilities).</summary>
public interface IResponsibilityDirectory
{
    Task<IReadOnlyList<Guid>> ApproversAsync(Guid employeeId, CancellationToken cancellationToken);
}

/// <summary>Publishes messages together with the current unit of work (outbox).</summary>
public interface IWorkforceMessages
{
    void Publish(IEvent message);

    void Send(ICommand message);
}
