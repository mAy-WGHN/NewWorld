using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Behind a reverse proxy (App Service, gateway) the caller's address and the scheme and host come from <c>X-Forwarded-*</c>. Without this
/// every caller would share the proxy's address, which breaks per-client limits (login, public forms). A header is only believed when it comes
/// from a proxy that is configured: <c>ForwardedHeaders:KnownProxies</c> (addresses) and <c>ForwardedHeaders:KnownNetworks</c> (CIDR, for example
/// <c>10.0.0.0/8</c>). Only the last entry counts (ForwardLimit 1). Without any configuration nothing is trusted and the middleware is not added,
/// unless the service asks to trust any proxy then (the public Portal does, as before).
/// </summary>
public static class ForwardedHeadersExtensions
{
    public const string ConfigurationSection = "ForwardedHeaders";

    private sealed class Enabled;

    public static IServiceCollection AddConfiguredForwardedHeaders(this IServiceCollection services, IConfiguration configuration, bool trustAnyProxyWhenUnconfigured = false)
    {
        var proxies = Values(configuration, "KnownProxies");
        var networks = Values(configuration, "KnownNetworks");
        if (proxies.Count == 0 && networks.Count == 0 && !trustAnyProxyWhenUnconfigured)
        {
            return services;
        }

        services.AddSingleton<Enabled>();
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            options.ForwardLimit = 1;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            foreach (var proxy in proxies)
            {
                options.KnownProxies.Add(IPAddress.Parse(proxy));
            }

            foreach (var network in networks)
            {
                options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
            }
        });
        return services;
    }

    /// <summary>Adds the forwarded headers middleware if <see cref="AddConfiguredForwardedHeaders"/> turned it on. Call first in the pipeline.</summary>
    public static WebApplication UseConfiguredForwardedHeaders(this WebApplication app)
    {
        if (app.Services.GetService<Enabled>() is not null)
        {
            app.UseForwardedHeaders();
        }

        return app;
    }

    private static List<string> Values(IConfiguration configuration, string name) =>
        [.. (configuration.GetSection($"{ConfigurationSection}:{name}").Get<string[]>() ?? []).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim())];
}
