using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shared.Testing;

namespace Shared.Testing.Tests;

public class FakeTimeTests
{
    private static readonly DateTimeOffset Start = new(2028, 2, 28, 23, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Replaces_existing_time_provider_registrations()
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);

        var fake = services.UseFakeTimeProvider(Start);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<TimeProvider>().ShouldBeSameAs(fake);
        provider.GetRequiredService<FakeTimeProvider>().ShouldBeSameAs(fake);
        provider.GetServices<TimeProvider>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Advancing_the_fake_moves_the_registered_clock()
    {
        var services = new ServiceCollection();
        var fake = services.UseFakeTimeProvider(Start);
        using var provider = services.BuildServiceProvider();

        fake.Advance(TimeSpan.FromHours(2)); // crosses midnight into the leap day

        provider.GetRequiredService<TimeProvider>().GetUtcNow().ShouldBe(new DateTimeOffset(2028, 2, 29, 1, 0, 0, TimeSpan.Zero));
    }
}
