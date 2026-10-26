using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Authorization;

namespace Access.Infrastructure.Login;

public static class AccessAuthentication
{
    public const string SelectorScheme = "AccessAuth";
    public const string OwnTokenScheme = "AccessToken";

    /// <summary>
    /// Two kinds of callers: people with tokens issued by Access itself (validated with its own key) and other services with
    /// tokens signed by their own key (<c>ServiceAuth:Peers</c>, optional). A policy scheme picks by the token's issuer prefix
    /// (<see cref="ServiceTokens.IssuerPrefix"/>), so neither kind can pass as the other: a token in the wrong scheme fails validation there.
    /// </summary>
    public static IServiceCollection AddAccessAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddServiceIdentity(configuration);
        var acceptsServices = ServiceAuthenticationExtensions.AcceptsServices(configuration);

        var authentication = services.AddAuthentication(SelectorScheme);
        if (acceptsServices)
        {
            authentication.AddServiceKeyScheme();
        }

        authentication
            .AddPolicyScheme(SelectorScheme, SelectorScheme, options => options.ForwardDefaultSelector = context =>
                acceptsServices && ServiceAuthenticationExtensions.IsServiceRequest(context)
                    ? ServiceAuthenticationExtensions.ServiceKeyScheme
                    : OwnTokenScheme)
            .AddJwtBearer(OwnTokenScheme, options => options.MapInboundClaims = false);

        services.AddOptions<JwtBearerOptions>(OwnTokenScheme).Configure<AccessTokenIssuer>((options, issuer) =>
            options.TokenValidationParameters = issuer.ValidationParameters);
        return services;
    }
}
