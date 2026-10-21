using Shared.Messaging;

namespace BcIntegration.Contracts;

/// <summary>
/// Request to delete or anonymise all personal data of an employee in every service holding a copy.
/// Published after a (for now manually triggered) release; retention periods are out of scope.
/// </summary>
/// <remarks>Publisher: BC Integration. Consumers: every service with employee copies (Workforce, Portal, Access, Notifications).</remarks>
/// <param name="MessageId">Unique message ID (idempotency).</param>
/// <param name="CorrelationId">Correlates all messages of one flow.</param>
/// <param name="OccurredAt">Time of the release (TimeProvider).</param>
/// <param name="EmployeeId">System-wide internal ID of the person whose data is to be erased.</param>
[Sensitivity(SensitivityClass.OrganizationData)]
public sealed record EmployeeDataErasureRequested(
    Guid MessageId,
    Guid CorrelationId,
    DateTimeOffset OccurredAt,
    Guid EmployeeId) : IEvent;
