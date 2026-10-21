namespace Shared.Messaging;

/// <summary>What a transport sends. The payload is opaque to the transport and must never be logged.</summary>
public sealed record OutboundMessage(
    Guid MessageId,
    Guid CorrelationId,
    string MessageType,
    MessageKind Kind,
    string Destination,
    string Payload,
    DateTimeOffset OccurredAt);
