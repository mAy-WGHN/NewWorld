using Shared.Messaging;

namespace BcIntegration.Contracts;

/// <summary>
/// An employee leaves (derived from the exit date of the BC employee card). Consumers act at <paramref name="ExitDate"/>,
/// not on receipt (e.g. Access withdraws permissions on the exit date).
/// </summary>
/// <remarks>Publisher: BC Integration. Consumers: Workforce, Portal, Access.</remarks>
/// <param name="MessageId">Unique message ID (idempotency).</param>
/// <param name="CorrelationId">Correlates all messages of one flow.</param>
/// <param name="OccurredAt">Time of the detection (TimeProvider).</param>
/// <param name="EmployeeId">System-wide internal ID.</param>
/// <param name="ExitDate">Austrittsdatum (last working day).</param>
[Sensitivity(SensitivityClass.OrganizationData)]
public sealed record EmployeeLeft(
    Guid MessageId,
    Guid CorrelationId,
    DateTimeOffset OccurredAt,
    Guid EmployeeId,
    DateOnly ExitDate) : IEvent;
