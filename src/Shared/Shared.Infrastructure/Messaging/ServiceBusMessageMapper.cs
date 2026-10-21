using Azure.Messaging.ServiceBus;

namespace Shared.Infrastructure.Messaging;

public static class ServiceBusMessageMapper
{
    public static ServiceBusMessage ToServiceBusMessage(OutboundMessage message) =>
        new(message.Payload)
        {
            MessageId = message.MessageId.ToString(),
            CorrelationId = message.CorrelationId.ToString(),
            Subject = message.MessageType,
            ContentType = "application/json",
            ApplicationProperties =
            {
                ["OccurredAt"] = message.OccurredAt,
                ["Kind"] = message.Kind.ToString(),
            },
        };
}
