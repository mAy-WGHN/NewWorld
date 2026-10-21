using Shared.Messaging;

namespace BcIntegration.Contracts;

/// <summary>
/// A write to Business Central succeeded. The owner of the entity sets its BC sync status to <c>Synced</c>.
/// Delivered through a subscription filtered on <paramref name="SourceService"/>, so each service sees only its own results.
/// </summary>
/// <remarks>Publisher: BC Integration. Consumers: the requesting service (filtered by <c>SourceService</c>), currently Workforce.</remarks>
/// <param name="MessageId">Unique message ID (idempotency).</param>
/// <param name="CorrelationId">Correlation ID of the originating command.</param>
/// <param name="OccurredAt">Time of the successful write (TimeProvider).</param>
/// <param name="SourceService">Service that requested the write.</param>
/// <param name="EntityType">Kind of the written entity.</param>
/// <param name="EntityId">Internal ID of the entity (external ID in BC).</param>
/// <param name="Version">Version of the entity that was written.</param>
/// <param name="BcSystemId">SystemId of the record in Business Central.</param>
[Sensitivity(SensitivityClass.OrganizationData)]
public sealed record BcSyncSucceeded(
    Guid MessageId,
    Guid CorrelationId,
    DateTimeOffset OccurredAt,
    SourceService SourceService,
    BcSyncEntityType EntityType,
    Guid EntityId,
    long Version,
    Guid BcSystemId) : IEvent;
