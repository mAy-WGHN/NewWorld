using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace Shared.Testing;

public static class FakeTime
{
    /// <summary>
    /// Replaces every <see cref="TimeProvider"/> registration with one <see cref="FakeTimeProvider"/> (also resolvable as itself),
    /// so a test can move time with <see cref="FakeTimeProvider.Advance"/>.
    /// </summary>
    public static FakeTimeProvider UseFakeTimeProvider(this IServiceCollection services, DateTimeOffset start)
    {
        var fake = new FakeTimeProvider(start);
        services.RemoveAll<TimeProvider>();
        services.AddSingleton<TimeProvider>(fake);
        services.AddSingleton(fake);
        return fake;
    }
}
