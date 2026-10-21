using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Benachrichtigungen.Common.Api;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Benachrichtigungen.Authentication;

/// <summary>
/// Section "Sources": Wer Ereignisse an diesen Dienst melden darf (Portal, HR-Tool, …). Je Absender ein eigener API-Schlüssel:
/// <c>Sources__Keys__portal=…</c>. Der Absender sendet ihn im Header <c>X-Api-Key</c>; sein Name gilt als Quelle des Ereignisses.
/// </summary>
public class SourcesOptions
{
    public const string SectionName = "Sources";

    public Dictionary<string, string> Keys { get; set; } = [];
}

public static class SourceKeyDefaults
{
    public const string Scheme = "SourceKey";
    public const string HeaderName = "X-Api-Key";
}

/// <summary>Prüft den Header <c>X-Api-Key</c> gegen die konfigurierten Schlüssel (zeitkonstant) und meldet den Namen des Absenders als Identität.</summary>
public class SourceKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> schemeOptions, ILoggerFactory loggerFactory, UrlEncoder encoder, IOptions<SourcesOptions> sources)
    : AuthenticationHandler<AuthenticationSchemeOptions>(schemeOptions, loggerFactory, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var supplied = Request.Headers[SourceKeyDefaults.HeaderName].ToString();
        if (string.IsNullOrEmpty(supplied)) return Task.FromResult(AuthenticateResult.NoResult());

        var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
        string? source = null;
        // Alle Schlüssel prüfen, ohne beim ersten Treffer abzubrechen: gleichbleibende Laufzeit.
        foreach (var (name, key) in sources.Value.Keys)
        {
            if (string.IsNullOrEmpty(key)) continue;
            if (CryptographicOperations.FixedTimeEquals(suppliedBytes, Encoding.UTF8.GetBytes(key))) source = name;
        }

        if (source is null) return Task.FromResult(AuthenticateResult.Fail("Ungültiger API-Schlüssel."));

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, source)], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
        ApiErrorWriter.WriteAsync(Context, StatusCodes.Status401Unauthorized, "API-Schlüssel fehlt oder ist ungültig.");
}

public static class AuthSetup
{
    /// <summary>
    /// Anmeldung der User: das JWT, das "Rollen-Rechte" ausstellt, wird hier nur <b>geprüft</b> (öffentlicher Schlüssel bzw. HS256-Fallback). Dieser Dienst hat keine
    /// Benutzer-Tabelle; der User ist die ID im Claim <c>sub</c>. Ein Token ohne <c>sub</c> (altes Symfony-Token) muss neu ausgestellt werden (neu anmelden).
    /// Ereignisse der anderen Anwendungen kommen mit einem API-Schlüssel (<see cref="SourceKeyDefaults.Scheme"/>).
    /// </summary>
    public static IServiceCollection AddNotificationAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection("Jwt").Get<JwtSettings>() ?? new JwtSettings();
        var keys = JwtKeys.Create(settings);

        services.AddSingleton(keys);
        services.Configure<SourcesOptions>(configuration.GetSection(SourcesOptions.SectionName));

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
                    OnTokenValidated = context =>
                    {
                        if (!Guid.TryParse(context.Principal?.FindFirst("sub")?.Value, out _))
                            context.Fail("Das Token enthält keine User-ID. Bitte neu anmelden.");
                        return Task.CompletedTask;
                    },

                    // 401-Meldungen wie bei den anderen Diensten, im API-Envelope.
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
            })
            .AddScheme<AuthenticationSchemeOptions, SourceKeyAuthenticationHandler>(SourceKeyDefaults.Scheme, null);

        services.AddAuthorization();
        return services;
    }

    /// <summary>Standard "geschlossen": Eine Route ohne eigene Angabe verlangt eine Anmeldung per JWT <i>oder</i> API-Schlüssel; die Routen legen dann ihre Art fest.</summary>
    public static AuthorizationPolicy DefaultPolicy { get; } =
        new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme, SourceKeyDefaults.Scheme).RequireAuthenticatedUser().Build();
}
