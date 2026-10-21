using Portal.Common.Api;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace Portal.Authentication;

public static class AuthSetup
{
    public static IServiceCollection AddIrsAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection("Jwt").Get<JwtSettings>() ?? new JwtSettings();
        var keys = JwtKeys.Create(settings);

        services.AddSingleton(keys);
        services.AddSingleton(TimeProvider.System);
        services.Configure<AccessOptions>(configuration.GetSection(AccessOptions.SectionName));
        services.AddMemoryCache();
        services.AddHttpClient(HttpAccessDirectory.HttpClientName);
        services.AddSingleton<IAccessDirectory, HttpAccessDirectory>();
        services.AddHostedService<CatalogRegistration>();

        // Das Token stellt Rollen-Rechte aus; hier wird nur die Signatur geprüft. Ob der User noch aktiv ist und was er darf,
        // klärt Rollen-Rechte bei jeder Rechteprüfung (PrivilegeHandler).
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    IssuerSigningKey = keys.ValidationKey,
                    ValidAlgorithms = [keys.Algorithm],
                    NameClaimType = "username",
                    ClockSkew = TimeSpan.FromMinutes(1)
                };

                options.Events = new JwtBearerEvents
                {
                    OnForbidden = context =>
                        ApiErrorWriter.WriteAsync(context.HttpContext, StatusCodes.Status403Forbidden, AuthMessages.ForEndpoint(context.HttpContext.GetEndpoint())),

                    // 401-Meldungen wie bei Lexik, im API-Envelope.
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        var message = context.AuthenticateFailure switch
                        {
                            SecurityTokenExpiredException => "Expired JWT Token",
                            null when string.IsNullOrEmpty(context.Request.Headers.Authorization) => "JWT Token not found",
                            _ => "Invalid JWT Token"
                        };
                        await ApiErrorWriter.WriteAsync(context.HttpContext, StatusCodes.Status401Unauthorized, message);
                    }
                };
            });

        // Rechte prüft HasPrivilegeAttribute (403 bei fehlendem Recht). Dass jede Controller-Route eines davon oder [AllowAnonymous] trägt,
        // erzwingt der globale Filter in AddControllers (Program.cs). Bewusst keine FallbackPolicy: Sie würde auch unbekannte Routen mit 401 statt 404 beantworten.
        services.AddHttpContextAccessor();
        services.AddSingleton<IAuthorizationPolicyProvider, PrivilegePolicyProvider>();
        services.AddScoped<ScopeContext>();
        services.AddScoped<IAuthorizationHandler, PrivilegeHandler>();
        return services;
    }
}
