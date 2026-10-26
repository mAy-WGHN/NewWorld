using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Shared.Authorization;

/// <summary>
/// Validation of the person tokens issued by Access (discovery/JWKS at the Access address). Authority and audience come from
/// configuration only.
/// </summary>
public static class SharedJwtAuthentication
{
    public const string ConfigurationSection = "Authentication";

    public static IServiceCollection AddSharedJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(ConfigurationSection);
        var authority = section["Authority"];
        if (string.IsNullOrWhiteSpace(authority))
        {
            throw new InvalidOperationException($"{ConfigurationSection}:Authority (the address of Access) is not configured.");
        }

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
                // Local landscape only: Access runs on plain http there. Production keeps the default (https).
                options.RequireHttpsMetadata = section.GetValue("RequireHttpsMetadata", true);
                options.MapInboundClaims = false; // keep claim names (employee_id) for AppClaimsTransformation
                options.TokenValidationParameters.ValidateIssuer = true;
                options.TokenValidationParameters.ValidateAudience = true;
                options.TokenValidationParameters.ValidateLifetime = true;
            });
        return services;
    }
}
