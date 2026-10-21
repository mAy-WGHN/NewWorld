using System.Security.Claims;
using Rollen_Rechte.Common.Api;
using Rollen_Rechte.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Rollen_Rechte.Authentication;

public static class AuthSetup
{
    public static IServiceCollection AddAccessAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection("Jwt").Get<JwtSettings>() ?? new JwtSettings();
        var keys = JwtKeys.Create(settings);

        services.Configure<JwtSettings>(configuration.GetSection("Jwt"));
        services.AddSingleton(keys);
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<JwtTokenService>();
        services.AddSingleton<PasswordService>();
        services.AddSingleton<LoginThrottle>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = !string.IsNullOrEmpty(settings.Issuer),
                    ValidIssuer = settings.Issuer,
                    ValidateAudience = !string.IsNullOrEmpty(settings.Audience),
                    ValidAudience = settings.Audience,
                    IssuerSigningKey = keys.ValidationKey,
                    ValidAlgorithms = [keys.Algorithm],
                    NameClaimType = "username",
                    ClockSkew = TimeSpan.FromMinutes(1)
                };

                options.Events = new JwtBearerEvents
                {
                    // Wie Symfony: der User wird bei JEDEM Request per username geladen. Deaktivierte User sind
                    // damit sofort ausgesperrt, auch mit gültigem Token. Tokens des Symfony-Backends tragen nur
                    // den username-Claim, daher wird die User-ID hier als "sub"-Claim ergänzt.
                    OnTokenValidated = async context =>
                    {
                        var username = context.Principal?.FindFirst("username")?.Value;
                        if (string.IsNullOrEmpty(username))
                        {
                            context.Fail("Ungültiges Token.");
                            return;
                        }

                        var db = context.HttpContext.RequestServices.GetRequiredService<AccessDbContext>();
                        var user = await db.Users.AsNoTracking()
                            .Where(u => u.Username == username)
                            .Select(u => new { u.Id, u.Active })
                            .FirstOrDefaultAsync(context.HttpContext.RequestAborted);

                        if (user is not { Active: true })
                        {
                            context.Fail("Benutzer ist deaktiviert.");
                            return;
                        }

                        if (context.Principal!.FindFirst("sub") is null) // ältere Tokens (Symfony) tragen nur den username
                            (context.Principal.Identity as ClaimsIdentity)?.AddClaim(new Claim("sub", user.Id.ToString()));
                    },

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

        // Rechte prüft HasPrivilegeAttribute (403 bei fehlendem Recht). Dass jede Controller-Route eines davon, die
        // Policy "authenticated" oder [AllowAnonymous] trägt, erzwingt der globale Filter in AddControllers (Program.cs).
        // Bewusst keine FallbackPolicy: sie würde auch unbekannte Routen mit 401 statt 404 beantworten.
        services.AddAuthorization(options =>
        {
            options.AddPolicy(AuthPolicies.Authenticated, policy => policy.RequireAuthenticatedUser());
        });
        services.AddHttpContextAccessor();
        services.AddSingleton<IAuthorizationPolicyProvider, PrivilegePolicyProvider>();
        services.AddScoped<PrivilegeService>();
        services.AddScoped<ScopeContext>();
        services.AddSingleton<PrivilegeCatalogService>();
        services.AddScoped<AclGuard>();
        services.AddScoped<IAuthorizationHandler, PrivilegeHandler>();
        return services;
    }
}
