using Shared.Messaging;

namespace Workforce.Contracts;

/// <summary>
/// An employee is (un)available for a period. Neutralised: deliberately without reason or absence type
/// (sick/vacation stays with Workforce; details only via an authorised call with <c>Workforce.AbsenceDetails.Read</c>).
/// </summary>
/// <remarks>Publisher: Workforce. Consumers: Portal (optional, prepared only).</remarks>
/// <param name="MessageId">Unique message ID (idempotency).</param>
/// <param name="CorrelationId">Correlates all messages of one flow.</param>
/// <param name="OccurredAt">Time of the change (TimeProvider).</param>
/// <param name="EmployeeId">Person concerned.</param>
/// <param name="From">First day of the period.</param>
/// <param name="To">Last day of the period; <c>null</c> for "until further notice".</param>
/// <param name="Status">Availability in the period, no reason.</param>
[Sensitivity(SensitivityClass.SensitiveNeutralized)]
public sealed record EmployeeAvailabilityChanged(
    Guid MessageId,
    Guid CorrelationId,
    DateTimeOffset OccurredAt,
    Guid EmployeeId,
    DateOnly From,
    DateOnly? To,
    AvailabilityStatus Status) : IEvent;
