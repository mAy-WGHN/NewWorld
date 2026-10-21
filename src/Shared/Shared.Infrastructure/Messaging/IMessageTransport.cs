

namespace Shared.Infrastructure.Messaging;

/// <summary>
/// Delivers outbox messages. Implementations (Azure Service Bus, later possibly HTTP) must throw if the
/// message was not accepted, and must not put the payload into exception messages or logs.
/// </summary>
public interface IMessageTransport
{
    Task SendAsync(OutboundMessage message, CancellationToken cancellationToken);
}
