using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace HrTool_API.Authentication;

/// <summary>Section "Access": Adresse des Dienstes "Rollen-Rechte", Name dieser Anwendung dort und der Schlüssel, den er dafür vergeben hat (dort: <c>Apps__Keys__{AppKey}</c>).</summary>
public class AccessOptions
{
    public const string SectionName = "Access";

    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Name dieser Anwendung bei "Rollen-Rechte" (Teil der Adresse und des Schlüssels).</summary>
    public string AppKey { get; set; } = "hr";

    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Wie lange die aufgelösten Rechte eines Tokens zwischengespeichert werden; ein Entzug wirkt spätestens danach.</summary>
    public int CacheSeconds { get; set; } = 10;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl);
}

/// <summary>Rollen-Rechte ist nicht erreichbar oder seine Antwort ist unbrauchbar: Die Rechte lassen sich nicht klären (fail-closed, 503).</summary>
public class AccessUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Was ein User darf: die konkreten Rechte (Platzhalter aufgelöst) und je Recht seine Reichweite.</summary>
public sealed record UserAccess(IReadOnlySet<string> Privileges, IReadOnlyDictionary<string, ScopeSet> Scopes);

/// <summary>Die Rechte des angemeldeten Users; die Wahrheit liegt bei Rollen-Rechte, das HR-Tool setzt sie nur durch.</summary>
public interface IAccessDirectory
{
    /// <summary>Löst die Rechte zum Token auf. <c>null</c>, wenn Rollen-Rechte das Token ablehnt (ungültig, abgelaufen, User deaktiviert).</summary>
    /// <exception cref="AccessUnavailableException">Rollen-Rechte ist nicht erreichbar.</exception>
    Task<UserAccess?> ResolveAsync(string token, CancellationToken ct);
}

/// <summary>Fragt <c>GET /api/me/privileges</c> mit dem Token des Users. Das Ergebnis wird kurz je Token zwischengespeichert (<see cref="AccessOptions.CacheSeconds"/>).</summary>
public class HttpAccessDirectory(IHttpClientFactory httpClientFactory, IMemoryCache cache, IOptions<AccessOptions> options, ILogger<HttpAccessDirectory> logger) : IAccessDirectory
{
    public const string HttpClientName = "Access";
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<UserAccess?> ResolveAsync(string token, CancellationToken ct)
    {
        var o = options.Value;
        if (!o.IsConfigured) throw new AccessUnavailableException("Der Zugang zu Rollen-Rechte ist nicht konfiguriert.");

        // Der Schlüssel ist ein Hash: Das Token selbst bleibt nicht im Speicher liegen.
        var key = "access:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        if (cache.TryGetValue(key, out UserAccess? cached)) return cached;

        var access = await FetchAsync(o, token, ct);
        if (access is not null && o.CacheSeconds > 0) cache.Set(key, access, TimeSpan.FromSeconds(o.CacheSeconds));
        return access;
    }

    private async Task<UserAccess?> FetchAsync(AccessOptions o, string token, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(CallTimeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(o.BaseUrl.TrimEnd('/') + "/"), "api/me/privileges"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(request, timeout.Token);
            if (response.StatusCode == HttpStatusCode.Unauthorized) return null;
            if (response.StatusCode != HttpStatusCode.OK)
            {
                logger.LogWarning("Rollen-Rechte antwortete auf die Rechteabfrage mit {StatusCode}.", (int)response.StatusCode);
                throw new AccessUnavailableException($"Rollen-Rechte antwortete mit {(int)response.StatusCode}.");
            }

            var envelope = await response.Content.ReadFromJsonAsync<Envelope>(Json, timeout.Token);
            if (envelope is not { Success: true, Data: { } data }) throw new AccessUnavailableException("Die Antwort von Rollen-Rechte ist unbrauchbar.");

            var scopes = data.Scopes.ToDictionary(
                s => s.Key,
                s => s.Value.All ? ScopeSet.Everything : ScopeSet.Of(s.Value.LocationIds),
                StringComparer.Ordinal);
            return new UserAccess(data.Privileges.ToHashSet(StringComparer.Ordinal), scopes);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Rollen-Rechte ist nicht erreichbar ({ErrorType}).", ex.GetType().Name);
            throw new AccessUnavailableException("Rollen-Rechte ist nicht erreichbar.", ex);
        }
    }

    private sealed record Envelope(bool Success, Payload? Data);

    private sealed record Payload(List<string> Privileges, Dictionary<string, ScopePayload> Scopes);

    // Weitere Dimensionen (z. B. reportIds fürs Portal) kennt das HR-Tool nicht: Sie werden ignoriert und gewähren nichts (fail-closed).
    private sealed record ScopePayload(bool All, List<Guid> LocationIds);
}
