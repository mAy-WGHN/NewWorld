using Shared.Messaging;

namespace Access.Contracts;

/// <summary>
/// Who is currently responsible for a person, per topic (manually maintained in Access, e.g. the store manager for everyday
/// matters, an auditor for audit instructions). Used for approval chains and notification recipients, not for permissions.
/// Sent after any change, including the start or end of a time-limited responsibility. <paramref name="Version"/> rises
/// strictly per person; consumers ignore older or equal versions.
/// </summary>
/// <remarks>Publisher: Access. Consumers: Workforce (further services on request).</remarks>
/// <param name="MessageId">Unique message ID (idempotency).</param>
/// <param name="CorrelationId">Correlates all messages of one flow.</param>
/// <param name="OccurredAt">Time of the change (TimeProvider).</param>
/// <param name="EmployeeId">Person the responsibilities refer to.</param>
/// <param name="Version">Strictly increasing per person.</param>
/// <param name="Responsibilities">Current responsibilities per topic; topics not listed have nobody responsible.</param>
[Sensitivity(SensitivityClass.OrganizationData)]
public sealed record ResponsibilitiesChanged(
    Guid MessageId,
    Guid CorrelationId,
    DateTimeOffset OccurredAt,
    Guid EmployeeId,
    long Version,
    IReadOnlyList<TopicResponsibility> Responsibilities) : IEvent;

/// <summary>Responsible persons for one topic.</summary>
/// <param name="Topic">Topic key, e.g. <c>absence.approval</c>.</param>
/// <param name="ResponsibleEmployeeIds">Persons responsible; usually one.</param>
public sealed record TopicResponsibility(string Topic, IReadOnlyList<Guid> ResponsibleEmployeeIds);
