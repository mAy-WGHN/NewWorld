using Shared.Messaging;

namespace BcIntegration.Contracts;

/// <summary>
/// A write to Business Central failed permanently for business reasons (no retry). The owner sets its BC sync status
/// to <c>Failed</c>. The error is a code only, never content from BC or personal data.
/// </summary>
/// <remarks>Publisher: BC Integration. Consumers: the requesting service (filtered by <c>SourceService</c>), currently Workforce.</remarks>
/// <param name="MessageId">Unique message ID (idempotency).</param>
/// <param name="CorrelationId">Correlation ID of the originating command.</param>
/// <param name="OccurredAt">Time of the failure (TimeProvider).</param>
/// <param name="SourceService">Service that requested the write.</param>
/// <param name="EntityType">Kind of the entity.</param>
/// <param name="EntityId">Internal ID of the entity.</param>
/// <param name="Version">Version of the entity that failed.</param>
/// <param name="ErrorCode">Reason without content.</param>
[Sensitivity(SensitivityClass.OrganizationData)]
public sealed record BcSyncFailed(
    Guid MessageId,
    Guid CorrelationId,
    DateTimeOffset OccurredAt,
    SourceService SourceService,
    BcSyncEntityType EntityType,
    Guid EntityId,
    long Version,
    BcSyncErrorCode ErrorCode) : IEvent;
