using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace BC_Anbindung.BusinessCentral;

/// <summary>Wird geworfen, wenn Azure AD kein gültiges Token für Business Central ausstellt.</summary>
public class BcAuthException(string message, string? details) : Exception(message)
{
    public string? Details { get; } = details;
}

/// <summary>
/// Holt Access-Tokens für Business Central per OAuth2-Client-Credentials-Flow und cacht sie bis
/// kurz vor Ablauf, damit nicht bei jedem BC-Call neu gegen Azure AD authentifiziert werden muss.
/// Als Singleton registriert (Program.cs), damit der Cache über Requests hinweg geteilt wird.
/// </summary>
public class BcAuthService(IHttpClientFactory httpClientFactory, IOptions<BcAuthOptions> options, ILogger<BcAuthService> logger)
    : IBcAuthService
{
    public const string HttpClientName = "BusinessCentralAuth";

    private readonly BcAuthOptions _options = options.Value;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private CachedToken? _cachedToken;

    public async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_cachedToken is { } cached && cached.ExpiresAt > DateTimeOffset.UtcNow)
            return cached.AccessToken;

        await _refreshLock.WaitAsync(ct);
        try
        {
            // Nach dem Warten auf den Lock erneut prüfen — eine andere Anfrage könnte das Token
            // in der Zwischenzeit bereits erneuert haben.
            if (_cachedToken is { } refreshed && refreshed.ExpiresAt > DateTimeOffset.UtcNow)
                return refreshed.AccessToken;

            var token = await RequestNewTokenAsync(ct);
            _cachedToken = token;
            return token.AccessToken;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>
    /// Eskalierender Backoff für die einzige Retry-Regel
    /// ("Bis zu 3 Versuche mit 2/5/10 Sekunden Abstand"): bis zu 3 Retries NACH dem ersten
    /// Fehlschlag, mit jeweils diesem Abstand davor — macht bis zu 4 Aufrufe insgesamt, bevor
    /// endgültig aufgegeben wird. Bewusst NUR für diesen einen, dokumentiert sporadischen
    /// Azure-AD-Fehlercode (`AADSTS7000215`) — jeder andere Fehler (falsches Secret, falscher
    /// Scope, echte Netzwerkstörung) schlägt weiterhin sofort fehl, sonst würde der Retry eine
    /// echte Fehlkonfiguration künstlich um bis zu 17 Sekunden verzögern statt sie sichtbar zu
    /// machen.
    /// </summary>
    private static readonly TimeSpan[] Aadsts7000215RetryDelays =
        [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)];

    private async Task<CachedToken> RequestNewTokenAsync(CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            var client = httpClientFactory.CreateClient(HttpClientName);

            using var request = new HttpRequestMessage(HttpMethod.Post, _options.ResolvedTokenEndpoint)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = _options.ClientId,
                    ["client_secret"] = _options.ClientSecret,
                    ["scope"] = _options.Scope,
                    ["grant_type"] = "client_credentials",
                })
            };

            using var response = await client.SendAsync(request, ct);

            if (response.IsSuccessStatusCode)
            {
                var payload = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct)
                    ?? throw new BcAuthException("BC-Token-Antwort konnte nicht gelesen werden.", null);

                // Sicherheitsabstand, damit ein laufender BC-Call nicht genau im Moment des Ablaufs
                // mit dem alten Token scheitert.
                var expiresAt = DateTimeOffset.UtcNow.AddSeconds(payload.ExpiresIn - 60);
                return new CachedToken(payload.AccessToken, expiresAt);
            }

            var errorBody = await response.Content.ReadAsStringAsync(ct);

            if (IsAadsts7000215(errorBody) && attempt <= Aadsts7000215RetryDelays.Length)
            {
                var delay = Aadsts7000215RetryDelays[attempt - 1];
                // Jeder Auftritt wird protokolliert (nicht nur der letzte) — der Retry darf eine
                // echte, dauerhafte Fehlkonfiguration nicht verschleiern.
                logger.LogWarning(
                    "BC-Token-Anfrage mit AADSTS7000215 abgelehnt (bekannte, sporadische Drosselung an diesem Mandanten) — Versuch {Attempt}/{MaxAttempts}, erneut in {DelaySeconds}s.",
                    attempt, Aadsts7000215RetryDelays.Length + 1, delay.TotalSeconds);
                await Task.Delay(delay, ct);
                continue;
            }

            logger.LogError("BC-Token-Anfrage fehlgeschlagen ({StatusCode}): {Body}", (int)response.StatusCode, errorBody);
            throw new BcAuthException($"BC-Token-Anfrage fehlgeschlagen ({(int)response.StatusCode}).", errorBody);
        }
    }

    /// <summary>
    /// Prüft primär das strukturierte <c>error_codes</c>-Array (offizielles Azure-AD-Format), zur
    /// Sicherheit zusätzlich per Text-Suche in <c>error_description</c> — falls der Fehlerbody aus
    /// irgendeinem Grund kein valides JSON ist, gilt das als "kein Drosselungsfall" (kein Retry).
    /// </summary>
    private static bool IsAadsts7000215(string errorBody)
    {
        try
        {
            var error = JsonSerializer.Deserialize<AadTokenErrorResponse>(errorBody);
            return error?.ErrorCodes?.Contains(7000215) == true
                   || error?.ErrorDescription?.Contains("AADSTS7000215", StringComparison.Ordinal) == true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed record CachedToken(string AccessToken, DateTimeOffset ExpiresAt);

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("token_type")] string TokenType);

    private sealed record AadTokenErrorResponse(
        [property: JsonPropertyName("error_codes")] int[]? ErrorCodes,
        [property: JsonPropertyName("error_description")] string? ErrorDescription);
}
