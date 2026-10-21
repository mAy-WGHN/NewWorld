using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace HrTool_API.Authentication;

/// <summary>
/// Meldet den Rechtekatalog dieser Anwendung (<see cref="PrivilegeCatalog"/>) bei Rollen-Rechte an (<c>PUT /api/apps/{appKey}/catalog</c>), damit man die Rechte dort in Rollen
/// vergeben kann. Läuft im Hintergrund und versucht es bis zum Erfolg wieder: Das HR-Tool startet auch, wenn Rollen-Rechte gerade nicht da ist.
/// </summary>
public class CatalogRegistration(IHttpClientFactory httpClientFactory, IOptions<AccessOptions> options, ILogger<CatalogRegistration> logger) : BackgroundService
{
    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(30);

    public static object Payload() => new
    {
        areas = PrivilegeCatalog.Areas.Select(a => new
        {
            key = a.Key, label = a.Label, definesReach = a.DefinesReach,
            actions = a.Actions.Select(x => new { privilege = x.Privilege, label = x.Label, scopable = x.Scopable })
        })
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        if (!o.IsConfigured || string.IsNullOrWhiteSpace(o.ApiKey))
        {
            logger.LogWarning("Rollen-Rechte ist nicht konfiguriert (Access__BaseUrl/Access__ApiKey): Der Rechtekatalog wird nicht angemeldet.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            if (await TryRegisterAsync(o, stoppingToken)) return;
            try
            {
                await Task.Delay(RetryAfter, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public async Task<bool> TryRegisterAsync(AccessOptions o, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Put,
                new Uri(new Uri(o.BaseUrl.TrimEnd('/') + "/"), $"api/apps/{Uri.EscapeDataString(o.AppKey)}/catalog"))
            {
                Content = JsonContent.Create(Payload())
            };
            request.Headers.Add("X-Api-Key", o.ApiKey);

            using var response = await httpClientFactory.CreateClient(HttpAccessDirectory.HttpClientName).SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation("Rechtekatalog bei Rollen-Rechte angemeldet ({Count} Rechte).", PrivilegeCatalog.All.Count);
                return true;
            }

            logger.LogWarning("Rollen-Rechte lehnte den Rechtekatalog mit {StatusCode} ab; neuer Versuch in {Seconds} s.", (int)response.StatusCode, RetryAfter.TotalSeconds);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Rollen-Rechte ist nicht erreichbar ({ErrorType}); der Rechtekatalog wird später angemeldet.", ex.GetType().Name);
        }

        return false;
    }
}
