using System.Net.Http.Json;
using System.Text.Json;
using Portal.Authentication;
using Portal.Common.Api;
using Portal.Contracts;
using Microsoft.Extensions.Options;

namespace Portal.Services;

/// <summary>
/// Die Filialen gehören Rollen-Rechte. Das Portal speichert an Meldungen nur ihre IDs und holt Namen und Archiv-Status von dort
/// (<c>GET /api/apps/{appKey}/locations</c>): die Liste wird kurz zwischengespeichert; ist Rollen-Rechte gerade nicht erreichbar, gilt die zuletzt bekannte weiter.
/// </summary>
public interface ILocationDirectory
{
    /// <summary>Alle Filialen (auch archivierte) nach ID.</summary>
    /// <exception cref="ApiException">503, wenn Rollen-Rechte nicht erreichbar ist und es noch keine Liste gibt.</exception>
    /// <param name="ct"></param>
    /// <param name="forceRefresh">Die Liste neu laden, auch wenn sie noch frisch ist (z. B. wenn eine ID unbekannt ist: eine Filiale kann gerade neu angelegt worden sein). Höchstens alle 5 Sekunden.</param>
    Task<IReadOnlyDictionary<Guid, LocationResponse>> GetAllAsync(CancellationToken ct, bool forceRefresh = false);
}

public class HttpLocationDirectory(IHttpClientFactory httpClientFactory, IOptions<AccessOptions> options, TimeProvider clock, ILogger<HttpLocationDirectory> logger) : ILocationDirectory
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MinRefreshInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _refresh = new(1, 1);

    // Singleton: der Zwischenspeicher gilt für alle Anfragen.
    private IReadOnlyDictionary<Guid, LocationResponse>? _locations;
    private DateTimeOffset _loadedAt;

    public async Task<IReadOnlyDictionary<Guid, LocationResponse>> GetAllAsync(CancellationToken ct, bool forceRefresh = false)
    {
        if (IsFresh(forceRefresh)) return _locations!;

        await _refresh.WaitAsync(ct);
        try
        {
            if (IsFresh(forceRefresh)) return _locations!;

            try
            {
                _locations = await FetchAsync(ct);
                _loadedAt = clock.GetUtcNow();
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException || ex is TaskCanceledException && !ct.IsCancellationRequested)
            {
                logger.LogWarning("Die Filialen von Rollen-Rechte konnten nicht geladen werden ({ErrorType}).", ex.GetType().Name);
                if (_locations is null)
                    throw new ApiException(StatusCodes.Status503ServiceUnavailable, "Die Filialen (Rollen-Rechte) sind nicht erreichbar.");
                _loadedAt = clock.GetUtcNow() - MaxAge + TimeSpan.FromSeconds(10); // zuletzt bekannte Liste weiterverwenden, in 10 s neu versuchen
            }

            return _locations!;
        }
        finally
        {
            _refresh.Release();
        }
    }

    private bool IsFresh(bool forceRefresh) =>
        _locations is not null && clock.GetUtcNow() - _loadedAt < (forceRefresh ? MinRefreshInterval : MaxAge);

    private async Task<IReadOnlyDictionary<Guid, LocationResponse>> FetchAsync(CancellationToken ct)
    {
        var o = options.Value;
        if (!o.IsConfigured || string.IsNullOrWhiteSpace(o.ApiKey)) throw new InvalidOperationException("Der Zugang zu Rollen-Rechte ist nicht konfiguriert.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(CallTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get,
            new Uri(new Uri(o.BaseUrl.TrimEnd('/') + "/"), $"api/apps/{Uri.EscapeDataString(o.AppKey)}/locations"));
        request.Headers.Add("X-Api-Key", o.ApiKey);

        using var response = await httpClientFactory.CreateClient(HttpAccessDirectory.HttpClientName).SendAsync(request, timeout.Token);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Rollen-Rechte antwortete mit {(int)response.StatusCode}.");

        var envelope = await response.Content.ReadFromJsonAsync<Envelope>(Json, timeout.Token);
        if (envelope is not { Success: true, Data: { } data }) throw new JsonException("Die Antwort von Rollen-Rechte ist unbrauchbar.");
        return data.ToDictionary(l => l.Id);
    }

    private sealed record Envelope(bool Success, List<LocationResponse>? Data);
}
