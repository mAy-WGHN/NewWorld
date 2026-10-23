using Testcontainers.ServiceBus;
using Xunit;

namespace Shared.Testing;

/// <summary>
/// Azure Service Bus emulator per test class or collection. The emulator's license (EULA) is accepted here; decided by
/// the project owner on 2026-10-21. Entities come from <c>ServiceBusEmulatorConfig.json</c>: separate queues per test
/// purpose so tests do not read each other's messages.
/// </summary>
public sealed class ServiceBusFixture : IAsyncLifetime
{
    /// <summary>Queue for transport tests (send and receive directly).</summary>
    public const string TransportQueue = "transport-test";

    /// <summary>Queue for consumer tests (the consumer host receives).</summary>
    public const string ConsumerQueue = "consumer-test";

    /// <summary>Topic with the unfiltered subscription <see cref="Subscription"/>.</summary>
    public const string Topic = "transport-test-topic";

    public const string Subscription = "all";

    private readonly ServiceBusContainer _container =
        new ServiceBusBuilder("mcr.microsoft.com/azure-messaging/servicebus-emulator:2.0.1")
            .WithAcceptLicenseAgreement(true)
            .WithConfig(Path.Combine(AppContext.BaseDirectory, "ServiceBusEmulatorConfig.json"))
            .Build();

    /// <summary>Emulator connection string (shared access key of the emulator; local only).</summary>
    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync() => await _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();
}
