using System.Collections.Concurrent;
using Azure.Messaging.ServiceBus;

namespace Shared.Infrastructure.Messaging;

/// <summary>
/// Sends outbox messages to Azure Service Bus: events to the topic, commands to the queue named by
/// <see cref="OutboundMessage.Destination"/>. Authentication is the client's (managed identity in Azure).
/// </summary>
public sealed class ServiceBusTransport(ServiceBusClient client) : IMessageTransport, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, ServiceBusSender> _senders = new(StringComparer.Ordinal);

    public async Task SendAsync(OutboundMessage message, CancellationToken cancellationToken)
    {
        var sender = _senders.GetOrAdd(message.Destination, client.CreateSender);
        await sender.SendMessageAsync(ServiceBusMessageMapper.ToServiceBusMessage(message), cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var sender in _senders.Values)
        {
            await sender.DisposeAsync();
        }

        await client.DisposeAsync();
    }
}
