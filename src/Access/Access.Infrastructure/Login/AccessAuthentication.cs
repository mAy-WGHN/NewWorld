using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Shared.Authorization;

namespace Access.Infrastructure.Login;

public static class AccessAuthentication
{
    public const string SelectorScheme = "AccessAuth";
    public const string OwnTokenScheme = "AccessToken";

    /// <summary>
    /// Two kinds of callers: people with tokens issued by Access itself (validated with its own key) and services with Entra tokens
    /// (configuration section <c>Authentication</c>, optional). A policy scheme picks by the token's issuer, so neither kind can
    /// pass as the other: a token from the wrong issuer fails validation in the chosen scheme.
    /// </summary>
    public static IServiceCollection AddAccessAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var entra = configuration.GetSection(SharedJwtAuthentication.ConfigurationSection).Exists();
        if (entra)
        {
            services.AddSharedJwtAuthentication(configuration);
        }

        services.AddAuthentication(SelectorScheme)
            .AddPolicyScheme(SelectorScheme, SelectorScheme, options => options.ForwardDefaultSelector = context =>
            {
                var header = context.Request.Headers.Authorization.ToString();
                if (entra && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                    && new JsonWebTokenHandler().CanReadToken(header[7..])
                    && new JsonWebTokenHandler().ReadJsonWebToken(header[7..]).Issuer != context.RequestServices.GetRequiredService<AccessTokenIssuer>().Issuer)
                {
                    return JwtBearerDefaults.AuthenticationScheme;
                }

                return OwnTokenScheme;
            })
            .AddJwtBearer(OwnTokenScheme, options => options.MapInboundClaims = false);

        services.AddOptions<JwtBearerOptions>(OwnTokenScheme).Configure<AccessTokenIssuer>((options, issuer) =>
            options.TokenValidationParameters = issuer.ValidationParameters);
        return services;
    }
}
