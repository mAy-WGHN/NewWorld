using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Shared.Authorization;

public static class ServiceAuthenticationExtensions
{
    public const string ServiceKeyScheme = "ServiceKey";
    public const string SelectorScheme = "PersonOrService";

    /// <summary>
    /// Own identity (<c>ServiceAuth:Name</c>/<c>PrivateKey</c>) for calling other services and the checker for incoming service tokens.
    /// Needs <see cref="AuthorizationServiceCollectionExtensions.AddSharedAuthorization"/>. The configuration is checked when the host starts.
    /// </summary>
    public static IServiceCollection AddServiceIdentity(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(ServiceAuthOptions.ConfigurationSection).Get<ServiceAuthOptions>() ?? new ServiceAuthOptions();
        services.TryAddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ServiceTokenIssuer>();
        services.TryAddSingleton<ServiceTokenValidator>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ServiceAuthStartupCheck>());
        return services;
    }

    /// <summary>
    /// Accepts calls from other services (<c>ServiceAuth:Peers</c>). With <paramref name="personScheme"/> (the scheme for people) a policy
    /// scheme picks by token: service tokens go to the service scheme, everything else to the person scheme. Without it, only services
    /// can authenticate (e.g. BC integration). Without configured peers nothing is added and service endpoints stay closed.
    /// Call after the person scheme has been registered.
    /// </summary>
    public static IServiceCollection AddServiceAuthentication(this IServiceCollection services, IConfiguration configuration, string? personScheme = null)
    {
        services.AddServiceIdentity(configuration);
        if (!AcceptsServices(configuration))
        {
            return services;
        }

        var authentication = services.AddAuthentication(options => options.DefaultScheme = personScheme is null ? ServiceKeyScheme : SelectorScheme)
            .AddServiceKeyScheme();
        if (personScheme is not null)
        {
            authentication.AddPolicyScheme(SelectorScheme, SelectorScheme, options => options.ForwardDefaultSelector = context =>
                IsServiceRequest(context) ? ServiceKeyScheme : personScheme);
        }

        return services;
    }

    public static AuthenticationBuilder AddServiceKeyScheme(this AuthenticationBuilder builder) =>
        builder.AddScheme<AuthenticationSchemeOptions, ServiceKeyAuthenticationHandler>(ServiceKeyScheme, _ => { });

    /// <summary>True if <c>ServiceAuth:Peers</c> is configured, i.e. this API accepts calls from other services.</summary>
    public static bool AcceptsServices(IConfiguration configuration) =>
        configuration.GetSection($"{ServiceAuthOptions.ConfigurationSection}:Peers").GetChildren().Any();

    /// <summary>True if the request carries a token that claims to come from a service (for custom scheme selectors).</summary>
    public static bool IsServiceRequest(HttpContext context) =>
        context.RequestServices.GetRequiredService<ServiceTokenValidator>().IsServiceToken(context.Request.Headers.Authorization.ToString());

    /// <summary>Calls from this client carry a token for <paramref name="targetService"/> (see <see cref="ServiceNames"/>).</summary>
    public static IHttpClientBuilder AddServiceToken(this IHttpClientBuilder builder, string targetService) =>
        builder.AddHttpMessageHandler(sp => new ServiceTokenHandler(sp.GetRequiredService<ServiceTokenIssuer>(), targetService));

    /// <summary>Builds the checker at start so a wrong key or role in the configuration stops the service right away.</summary>
    private sealed class ServiceAuthStartupCheck(ServiceTokenValidator validator) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _ = validator;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
