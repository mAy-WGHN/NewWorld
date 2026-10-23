using Azure.Messaging.ServiceBus;
using Shared.Infrastructure.Messaging;
using Shared.Messaging;
using Shared.Testing;

namespace Shared.Infrastructure.Tests;

/// <summary>The transport delivers to the real Service Bus emulator: commands to a queue, events to a topic.</summary>
[Collection(ServiceBusTestGroup.Name)]
public sealed class ServiceBusTransportTests(ServiceBusFixture serviceBus)
{
    private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(30);

    private static OutboundMessage Message(MessageKind kind, string destination) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "ThingChanged",
        kind,
        destination,
        """{"thingId":"00000000-0000-0000-0000-000000000001"}""",
        new DateTimeOffset(2026, 3, 29, 1, 30, 0, TimeSpan.Zero));

    [Fact]
    public async Task Command_arrives_in_the_queue_with_ids_and_body()
    {
        await using var client = new ServiceBusClient(serviceBus.ConnectionString);
        await using var transport = new ServiceBusTransport(client);
        await using var receiver = client.CreateReceiver(ServiceBusFixture.TransportQueue);
        var sent = Message(MessageKind.Command, ServiceBusFixture.TransportQueue);

        await transport.SendAsync(sent, CancellationToken.None);
        var received = await receiver.ReceiveMessageAsync(ReceiveTimeout);

        received.ShouldNotBeNull();
        received.MessageId.ShouldBe(sent.MessageId.ToString());
        received.CorrelationId.ShouldBe(sent.CorrelationId.ToString());
        received.Subject.ShouldBe("ThingChanged");
        received.Body.ToString().ShouldBe(sent.Payload);
        await receiver.CompleteMessageAsync(received);
    }

    [Fact]
    public async Task Event_reaches_a_topic_subscription()
    {
        await using var client = new ServiceBusClient(serviceBus.ConnectionString);
        await using var transport = new ServiceBusTransport(client);
        await using var receiver = client.CreateReceiver(ServiceBusFixture.Topic, ServiceBusFixture.Subscription);
        var sent = Message(MessageKind.Event, ServiceBusFixture.Topic);

        await transport.SendAsync(sent, CancellationToken.None);
        var received = await receiver.ReceiveMessageAsync(ReceiveTimeout);

        received.ShouldNotBeNull();
        received.MessageId.ShouldBe(sent.MessageId.ToString());
        received.ApplicationProperties["Kind"].ShouldBe("Event");
        await receiver.CompleteMessageAsync(received);
    }
}
