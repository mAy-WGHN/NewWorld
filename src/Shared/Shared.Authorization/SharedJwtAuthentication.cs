using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Shared.Authorization;

/// <summary>Standards-based JWT validation. Authority, audience and tenant come from configuration only.</summary>
public static class SharedJwtAuthentication
{
    public const string ConfigurationSection = "Authentication";

    public static IServiceCollection AddSharedJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(ConfigurationSection);
        var authority = Resolve(section);
        var audience = section["Audience"];
        if (string.IsNullOrWhiteSpace(audience))
        {
            throw new InvalidOperationException($"{ConfigurationSection}:Audience is not configured.");
        }

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = authority;
                options.Audience = audience;
                options.MapInboundClaims = false; // keep provider claim names (roles, oid) for AppClaimsTransformation
                options.TokenValidationParameters.ValidateIssuer = true;
                options.TokenValidationParameters.ValidateAudience = true;
                options.TokenValidationParameters.ValidateLifetime = true;
            });
        return services;
    }

    private static string Resolve(IConfigurationSection section)
    {
        var authority = section["Authority"];
        if (!string.IsNullOrWhiteSpace(authority))
        {
            return authority;
        }

        var tenant = section["TenantId"];
        if (!string.IsNullOrWhiteSpace(tenant))
        {
            return $"https://login.microsoftonline.com/{tenant}/v2.0";
        }

        throw new InvalidOperationException($"{ConfigurationSection}:Authority or {ConfigurationSection}:TenantId is not configured.");
    }
}
