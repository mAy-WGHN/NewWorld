using Shared.Messaging;

namespace Shared.Infrastructure.Messaging;

/// <summary>Outgoing message, stored in the same transaction as the state change that caused it.</summary>
public sealed class OutboxMessage
{
    /// <summary>Equals <see cref="IMessage.MessageId"/>.</summary>
    public Guid Id { get; set; }

    /// <summary>Message type name (no namespace), e.g. <c>EmployeeChanged</c>.</summary>
    public required string MessageType { get; set; }

    /// <summary>Topic (events) or queue (commands), see <see cref="MessagingNames"/>.</summary>
    public required string Destination { get; set; }

    public MessageKind Kind { get; set; }

    /// <summary>JSON body. Never written to logs.</summary>
    public required string Payload { get; set; }

    public Guid CorrelationId { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Set once the transport accepted the message.</summary>
    public DateTimeOffset? ProcessedAt { get; set; }

    public int Attempts { get; set; }
}
