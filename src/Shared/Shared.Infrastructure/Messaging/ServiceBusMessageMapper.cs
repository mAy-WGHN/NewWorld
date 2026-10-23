using Azure.Messaging.ServiceBus;

namespace Shared.Infrastructure.Messaging;

public static class ServiceBusMessageMapper
{
    /// <summary>
    /// Routing property for filtered subscriptions: results of BC syncs carry <c>SourceService</c> (e.g. "Workforce"); every
    /// sender gets only the answers to its own requests (SQL filter on the subscription, see infra/servicebus.bicep).
    /// </summary>
    public const string SourceServiceProperty = "SourceService";

    public static ServiceBusMessage ToServiceBusMessage(OutboundMessage message)
    {
        var result = new ServiceBusMessage(message.Payload)
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
        if (SourceService(message.Payload) is { } source)
        {
            result.ApplicationProperties[SourceServiceProperty] = source;
        }

        return result;
    }

    private static string? SourceService(string payload)
    {
        // Only the top-level field; the payload itself is never logged or copied elsewhere.
        using var document = System.Text.Json.JsonDocument.Parse(payload);
        return document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
            && document.RootElement.TryGetProperty("sourceService", out var value)
            && value.ValueKind == System.Text.Json.JsonValueKind.String
                ? value.GetString()
                : null;
    }
}
