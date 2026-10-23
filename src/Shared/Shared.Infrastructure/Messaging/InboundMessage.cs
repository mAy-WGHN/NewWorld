namespace Shared.Infrastructure.Messaging;

/// <summary>A received message as the transport hands it over. <see cref="Body"/> is never logged.</summary>
public sealed record InboundMessage(string? MessageId, string? MessageType, string Body, int DeliveryCount);

public enum DispatchOutcome
{
    Unknown = 0,
    Completed = 1,
    Duplicate = 2,
    Retry = 3,
    DeadLetter = 4,
}

/// <summary>What the transport does with the message. <see cref="Reason"/> is a content-free code.</summary>
public sealed record DispatchResult(DispatchOutcome Outcome, string? Reason = null);
