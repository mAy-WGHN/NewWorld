using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using BC_Anbindung.Common.Api;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace BC_Anbindung.Authentication;

/// <summary>
/// Section "Gateway": Wer diesen Dienst aufrufen darf. Je Aufrufer (z. B. "Identity", "Portal") ein eigener API-Schlüssel, damit sich einer
/// einzeln entziehen oder erneuern lässt: <c>Gateway__ApiKeys__Identity=…</c>. Der Dienst hält mächtige BC-Zugangsdaten und ist nur für die
/// eigenen Programme gedacht — nie öffentlich erreichbar machen.
/// </summary>
public class GatewayOptions
{
    public const string SectionName = "Gateway";

    public Dictionary<string, string> ApiKeys { get; set; } = [];

    /// <summary>Wie lange die Filialliste zwischengespeichert wird (0 = nie); schont BC.</summary>
    public int StoresCacheSeconds { get; set; } = 300;
}

public static class ApiKeyDefaults
{
    public const string Scheme = "ApiKey";
    public const string HeaderName = "X-Api-Key";
}

/// <summary>Prüft den Header <c>X-Api-Key</c> gegen die konfigurierten Schlüssel (zeitkonstant) und meldet den Namen des Aufrufers als Identität.</summary>
public class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> schemeOptions, ILoggerFactory loggerFactory, UrlEncoder encoder, IOptions<GatewayOptions> gateway)
    : AuthenticationHandler<AuthenticationSchemeOptions>(schemeOptions, loggerFactory, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyDefaults.HeaderName, out var header) || string.IsNullOrEmpty(header.ToString()))
            return Task.FromResult(AuthenticateResult.NoResult());

        var supplied = Encoding.UTF8.GetBytes(header.ToString());
        string? caller = null;
        // Alle Schlüssel prüfen, ohne beim ersten Treffer abzubrechen: gleichbleibende Laufzeit.
        foreach (var (name, key) in gateway.Value.ApiKeys)
        {
            if (string.IsNullOrEmpty(key)) continue;
            if (CryptographicOperations.FixedTimeEquals(supplied, Encoding.UTF8.GetBytes(key))) caller = name;
        }

        if (caller is null) return Task.FromResult(AuthenticateResult.Fail("Ungültiger API-Schlüssel."));

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, caller)], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
        ApiErrorWriter.WriteAsync(Context, StatusCodes.Status401Unauthorized, "API-Schlüssel fehlt oder ist ungültig.");
}
