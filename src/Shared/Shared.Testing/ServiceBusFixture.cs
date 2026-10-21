using Testcontainers.ServiceBus;
using Xunit;

namespace Shared.Testing;

/// <summary>
/// Azure Service Bus emulator per test class (use as <c>IClassFixture</c>). The emulator's license (EULA) is accepted
/// here; decided by the project owner on 2026-10-21. The emulator's default configuration provides the entities below.
/// </summary>
public sealed class ServiceBusFixture : IAsyncLifetime
{
    public const string DefaultQueue = "queue.1";
    public const string DefaultTopic = "topic.1";
    /// <summary>The default configuration's subscription without filter rules (subscription.1/.2 filter by correlation).</summary>
    public const string DefaultSubscription = "subscription.3";

    private readonly ServiceBusContainer _container =
        new ServiceBusBuilder("mcr.microsoft.com/azure-messaging/servicebus-emulator:2.0.1")
            .WithAcceptLicenseAgreement(true)
            .Build();

    /// <summary>Emulator connection string (shared access key of the emulator; local only).</summary>
    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync() => await _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();
}
