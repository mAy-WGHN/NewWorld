namespace Shared.Messaging;

/// <summary>Record of a handled message; makes receivers idempotent (Service Bus delivers at least once).</summary>
public sealed class InboxMessage
{
    public Guid MessageId { get; set; }

    /// <summary>Name of the handling consumer; one message may be handled once per consumer.</summary>
    public required string Consumer { get; set; }

    public DateTimeOffset ProcessedAt { get; set; }
}
