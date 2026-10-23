using Shared.Messaging;

namespace Shared.Infrastructure.Messaging;

/// <summary>
/// Handles one message type. Runs inside the inbox transaction: database changes and outbox messages written through the
/// service's DbContext commit together with the inbox entry. Throw <see cref="PermanentMessageFailureException"/> for
/// failures a redelivery cannot fix; any other exception leads to redelivery.
/// </summary>
public interface IMessageHandler<in TMessage> where TMessage : IMessage
{
    Task HandleAsync(TMessage message, CancellationToken cancellationToken);
}
