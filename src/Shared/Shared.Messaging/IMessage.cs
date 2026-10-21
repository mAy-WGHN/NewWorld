namespace Shared.Messaging;

/// <summary>Base of every message on the bus (events and commands).</summary>
public interface IMessage
{
    /// <summary>Unique ID; receivers use it for idempotency (at-least-once delivery).</summary>
    Guid MessageId { get; }

    /// <summary>Correlates all messages of one business flow.</summary>
    Guid CorrelationId { get; }

    /// <summary>Time of creation, taken from <see cref="TimeProvider"/>.</summary>
    DateTimeOffset OccurredAt { get; }
}
