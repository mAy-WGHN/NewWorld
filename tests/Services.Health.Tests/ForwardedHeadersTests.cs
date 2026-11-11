using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Services.Health.Tests;

/// <summary>The caller's address behind a proxy: believed only from configured proxies, nothing without configuration (unless asked to).</summary>
public sealed class ForwardedHeadersTests
{
    private static ServiceProvider Services(Dictionary<string, string?> settings, bool trustAny = false)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddConfiguredForwardedHeaders(configuration, trustAny);
        return services.BuildServiceProvider();
    }

    /// <summary>The address the application sees after the middleware, for a request from <paramref name="remote"/> that claims <paramref name="forwardedFor"/>.</summary>
    private static async Task<IPAddress?> SeenAsync(ServiceProvider services, string remote, string forwardedFor)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(remote);
        context.Request.Headers["X-Forwarded-For"] = forwardedFor;
        var middleware = new ForwardedHeadersMiddleware(_ => Task.CompletedTask, NullLoggerFactory.Instance, services.GetRequiredService<IOptions<ForwardedHeadersOptions>>());
        await middleware.Invoke(context);
        return context.Connection.RemoteIpAddress;
    }

    [Fact]
    public async Task The_header_of_a_configured_proxy_is_believed()
    {
        await using var services = Services(new() { ["ForwardedHeaders:KnownProxies:0"] = "10.0.0.5" });

        (await SeenAsync(services, "10.0.0.5", "203.0.113.7")).ShouldBe(IPAddress.Parse("203.0.113.7"));
    }

    [Fact]
    public async Task The_header_of_any_other_sender_is_ignored_so_a_client_cannot_pick_its_own_address()
    {
        await using var services = Services(new() { ["ForwardedHeaders:KnownProxies:0"] = "10.0.0.5" });

        (await SeenAsync(services, "198.51.100.9", "203.0.113.7")).ShouldBe(IPAddress.Parse("198.51.100.9"));
    }

    [Fact]
    public async Task A_configured_network_is_trusted_as_a_whole()
    {
        await using var services = Services(new() { ["ForwardedHeaders:KnownNetworks:0"] = "10.0.0.0/8" });

        (await SeenAsync(services, "10.20.30.40", "203.0.113.7")).ShouldBe(IPAddress.Parse("203.0.113.7"));
        (await SeenAsync(services, "11.0.0.1", "203.0.113.7")).ShouldBe(IPAddress.Parse("11.0.0.1"));
    }

    [Fact]
    public async Task Only_the_last_entry_of_a_chain_counts()
    {
        await using var services = Services(new() { ["ForwardedHeaders:KnownProxies:0"] = "10.0.0.5" });

        (await SeenAsync(services, "10.0.0.5", "1.1.1.1, 203.0.113.7")).ShouldBe(IPAddress.Parse("203.0.113.7"));
    }

    [Fact]
    public void Without_configuration_nothing_is_turned_on_unless_the_service_asks_to_trust_any_proxy()
    {
        using var off = Services([]);
        using var on = Services([], trustAny: true);

        off.GetService<IConfigureOptions<ForwardedHeadersOptions>>().ShouldBeNull();
        on.GetService<IConfigureOptions<ForwardedHeadersOptions>>().ShouldNotBeNull();
    }

    [Fact]
    public async Task A_service_that_trusts_any_proxy_believes_every_sender()
    {
        await using var services = Services([], trustAny: true);

        (await SeenAsync(services, "198.51.100.9", "203.0.113.7")).ShouldBe(IPAddress.Parse("203.0.113.7"));
    }
}
