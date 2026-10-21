using Shared.Messaging;

namespace BcIntegration.Contracts;

/// <summary>
/// Full current state of an employee from the Business Central employee card. Published only on a real change
/// (compared with the last known state), so consumers can keep their read-only copy up to date.
/// </summary>
/// <remarks>Publisher: BC Integration. Consumers: Workforce, Portal, Access.</remarks>
/// <param name="MessageId">Unique message ID (idempotency).</param>
/// <param name="CorrelationId">Correlates all messages of one flow.</param>
/// <param name="OccurredAt">Time of the change detection (TimeProvider).</param>
/// <param name="EmployeeId">System-wide internal ID, issued by BC Integration.</param>
/// <param name="PersonnelNumber">Personalnummer as in BC.</param>
/// <param name="Name">Display name. TODO(BC-Modell): first and last name separately?</param>
/// <param name="HomeStoreId">Hauptfiliale (internal store ID); <c>null</c> for employees without a store (e.g. head office).</param>
/// <param name="Position">Position code as in BC. TODO(BC-Modell): value list (which positions mean store/district manager, HR).</param>
/// <param name="EmploymentType">Arbeitsverhältnis-Typ code as in BC. TODO(BC-Modell): value list; becomes an enum once known.</param>
/// <param name="ContractedHoursPerWeek">Sollstunden per week. TODO(BC-Modell): field and unit in BC.</param>
/// <param name="EntryDate">Eintrittsdatum.</param>
/// <param name="ExitDate">Austrittsdatum; <c>null</c> while employed.</param>
[Sensitivity(SensitivityClass.OrganizationData)]
public sealed record EmployeeChanged(
    Guid MessageId,
    Guid CorrelationId,
    DateTimeOffset OccurredAt,
    Guid EmployeeId,
    string PersonnelNumber,
    string Name,
    Guid? HomeStoreId,
    string? Position,
    string? EmploymentType,
    decimal? ContractedHoursPerWeek,
    DateOnly? EntryDate,
    DateOnly? ExitDate) : IEvent;
