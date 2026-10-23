using Shared.Testing;

namespace Shared.Infrastructure.Tests;

/// <summary>One emulator for all Service Bus tests of this assembly (the emulator is heavy: it starts SQL Server too).</summary>
[CollectionDefinition(Name)]
public sealed class ServiceBusTestGroup : ICollectionFixture<ServiceBusFixture>
{
    public const string Name = "ServiceBus";
}
