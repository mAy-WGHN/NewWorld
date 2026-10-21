using Microsoft.EntityFrameworkCore;
using Shared.Messaging;

namespace Shared.Infrastructure.Messaging;

/// <summary>
/// Adds messages to the context's change tracker. They are saved by the caller's <c>SaveChanges</c> /
/// transaction, i.e. together with the state change, and only sent afterwards by <see cref="OutboxDispatcher{TContext}"/>.
/// </summary>
public sealed class Outbox<TContext>(TContext db) where TContext : DbContext
{
    public void Publish<T>(T message) where T : IEvent => Add(message, MessageKind.Event);

    public void Send<T>(T message) where T : ICommand => Add(message, MessageKind.Command);

    private void Add(IMessage message, MessageKind kind)
    {
        var type = message.GetType();
        db.Set<OutboxMessage>().Add(new OutboxMessage
        {
            Id = message.MessageId,
            MessageType = type.Name,
            Destination = MessagingNames.For(type),
            Kind = kind,
            Payload = MessageJson.Serialize(message),
            CorrelationId = message.CorrelationId,
            OccurredAt = message.OccurredAt,
        });
    }
}
