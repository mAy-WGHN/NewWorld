using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shared.Infrastructure.Messaging;
using Shared.Messaging;
using Shared.Testing;

namespace Shared.Infrastructure.Tests;

/// <summary>The consumer host receives from the emulator, runs the handler and dead-letters what cannot be processed.</summary>
[Collection(ServiceBusTestGroup.Name)]
public sealed class ServiceBusConsumerHostTests(ServiceBusFixture serviceBus, PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private async Task<(ServiceProvider Services, IHostedService Host, HandlerProbe Probe)> StartAsync()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var probe = new HandlerProbe();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<TestDbContext>(o => o.UseNpgsql(connectionString));
        services.AddSingleton(probe);
        services.AddSingleton(new MessagingOptions { MaxConcurrentCalls = 1 });
        services.AddSingleton(new ServiceBusClient(serviceBus.ConnectionString));
        services.AddMessageHandler<TestDbContext, SyncThing, SyncThingHandler>(MessageSource.Queue(ServiceBusFixture.ConsumerQueue));
        services.AddSingleton<ServiceBusConsumerHost>();
        var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<TestDbContext>().Database.EnsureCreatedAsync();
        }

        var host = provider.GetRequiredService<ServiceBusConsumerHost>();
        await host.StartAsync(CancellationToken.None);
        return (provider, host, probe);
    }

    [Fact]
    public async Task Delivers_a_sent_command_to_its_handler()
    {
        var (services, host, probe) = await StartAsync();
        try
        {
            var command = new SyncThing(Guid.NewGuid(), Guid.NewGuid(), new DateTimeOffset(2026, 10, 23, 8, 0, 0, TimeSpan.Zero), Guid.NewGuid());
            await new ServiceBusTransport(services.GetRequiredService<ServiceBusClient>()).SendAsync(
                new OutboundMessage(command.MessageId, command.CorrelationId, nameof(SyncThing), MessageKind.Command,
                    ServiceBusFixture.ConsumerQueue, MessageJson.Serialize(command), command.OccurredAt),
                CancellationToken.None);

            var handled = await probe.Handled.Task.WaitAsync(Timeout);

            handled.ShouldBe(command);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
            await services.DisposeAsync();
        }
    }

    [Fact]
    public async Task Unprocessable_message_lands_in_the_dead_letter_queue_with_a_reason_code()
    {
        var (services, host, probe) = await StartAsync();
        try
        {
            var client = services.GetRequiredService<ServiceBusClient>();
            await using (var sender = client.CreateSender(ServiceBusFixture.ConsumerQueue))
            {
                await sender.SendMessageAsync(new ServiceBusMessage("{broken") { MessageId = Guid.NewGuid().ToString(), Subject = nameof(SyncThing) });
            }

            await using var deadLetters = client.CreateReceiver(ServiceBusFixture.ConsumerQueue, new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter });
            var dead = await deadLetters.ReceiveMessageAsync(Timeout);

            dead.ShouldNotBeNull();
            dead.DeadLetterReason.ShouldBe("InvalidBody");
            probe.Calls.ShouldBe(0);
            await deadLetters.CompleteMessageAsync(dead);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
            await services.DisposeAsync();
        }
    }
}
