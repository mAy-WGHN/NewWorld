using Shared.Messaging;

namespace BcIntegration.Contracts;

/// <summary>An employee joins (derived from the entry date of the BC employee card). The full state follows in <see cref="EmployeeChanged"/>.</summary>
/// <remarks>Publisher: BC Integration. Consumers: Portal, Access.</remarks>
/// <param name="MessageId">Unique message ID (idempotency).</param>
/// <param name="CorrelationId">Correlates all messages of one flow.</param>
/// <param name="OccurredAt">Time of the detection (TimeProvider).</param>
/// <param name="EmployeeId">System-wide internal ID.</param>
/// <param name="EntryDate">Eintrittsdatum.</param>
/// <param name="HomeStoreId">Hauptfiliale; <c>null</c> for employees without a store.</param>
[Sensitivity(SensitivityClass.OrganizationData)]
public sealed record EmployeeHired(
    Guid MessageId,
    Guid CorrelationId,
    DateTimeOffset OccurredAt,
    Guid EmployeeId,
    DateOnly EntryDate,
    Guid? HomeStoreId) : IEvent;
