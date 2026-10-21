using Shared.Messaging;

namespace Workforce.Contracts;

/// <summary>A planned shift has nobody assigned (e.g. after an absence). Neutralised: no person, no reason.</summary>
/// <remarks>Publisher: Workforce. Consumers: none yet (prepared only).</remarks>
/// <param name="MessageId">Unique message ID (idempotency).</param>
/// <param name="CorrelationId">Correlates all messages of one flow.</param>
/// <param name="OccurredAt">Time of the detection (TimeProvider).</param>
/// <param name="ShiftId">Shift concerned.</param>
/// <param name="StoreId">Store of the shift.</param>
/// <param name="From">Start of the shift (with offset, so shifts across midnight and DST changes are exact).</param>
/// <param name="To">End of the shift.</param>
[Sensitivity(SensitivityClass.SensitiveNeutralized)]
public sealed record ShiftUncovered(
    Guid MessageId,
    Guid CorrelationId,
    DateTimeOffset OccurredAt,
    Guid ShiftId,
    Guid StoreId,
    DateTimeOffset From,
    DateTimeOffset To) : IEvent;
