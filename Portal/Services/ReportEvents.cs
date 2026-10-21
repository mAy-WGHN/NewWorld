using System.Net.Http.Json;
using System.Text.Json;
using Portal.Domain.Entities;
using Portal.Serialization;
using Microsoft.Extensions.Options;

namespace Portal.Services;

/// <summary>Section "Notifications": Adresse des Dienstes "Benachrichtigungen" und der Schlüssel, den er für das Portal vergeben hat (dort: <c>Sources__Keys__portal</c>).</summary>
public class NotificationsOptions
{
    public const string SectionName = "Notifications";

    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(ApiKey);
}

/// <summary>Das Portal meldet, was passiert ist; wer davon erfährt und wie, entscheidet der Dienst "Benachrichtigungen".</summary>
public interface IReportEvents
{
    /// <summary>Meldet eine neue Meldung. Fehler werden nur geloggt, nie weitergereicht: Die Meldung ist gespeichert und darf nicht an der Benachrichtigung scheitern.</summary>
    Task ReportCreatedAsync(Report report, CancellationToken ct);
}

/// <summary>
/// Sendet <c>report.created</c> an <c>POST /api/events</c>. Die Ereignis-ID ist die ID der Meldung: Ein erneutes Senden löst keine zweite Benachrichtigung aus.
/// Zwei Versuche; scheitern beide, steht der Fehler im Log (ohne Zwischenablage geht das Ereignis dann verloren — siehe CLAUDE.md).
/// </summary>
public class HttpReportEvents(IHttpClientFactory httpClientFactory, IOptions<NotificationsOptions> options, ILogger<HttpReportEvents> logger) : IReportEvents
{
    public const string HttpClientName = "Notifications";
    public const string EventType = "report.created";
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);
    private const int Attempts = 2;
    private static readonly JsonSerializerOptions Json = CreateJson();

    private static JsonSerializerOptions CreateJson()
    {
        var json = new JsonSerializerOptions();
        ApiJson.Configure(json);
        return json;
    }

    public async Task ReportCreatedAsync(Report report, CancellationToken ct)
    {
        var o = options.Value;
        if (!o.IsConfigured)
        {
            logger.LogWarning("Benachrichtigungen ist nicht konfiguriert (Notifications__BaseUrl/ApiKey): Meldung {ReportId} löst keine Benachrichtigung aus.", report.Id);
            return;
        }

        var body = new
        {
            id = report.Id,
            type = EventType,
            occurredAt = DateTime.SpecifyKind(report.CreatedAt, DateTimeKind.Utc),
            data = new
            {
                reportId = report.Id,
                category = report.Category,
                severity = report.Severity,
                description = report.Description,
                locationIds = report.LocationIds.ToList()
            }
        };

        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(CallTimeout);
                using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(o.BaseUrl.TrimEnd('/') + "/"), "api/events"))
                {
                    Content = JsonContent.Create(body, options: Json)
                };
                request.Headers.Add("X-Api-Key", o.ApiKey);

                using var response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(request, timeout.Token);
                if (response.IsSuccessStatusCode) return;

                // 4xx (außer Zeitüberschreitung/zu viele Anfragen) heißt: Der Dienst lehnt das Ereignis ab, Wiederholen hilft nicht.
                var retryable = (int)response.StatusCode >= 500 || (int)response.StatusCode is 408 or 429;
                logger.LogWarning("Benachrichtigungen lehnte das Ereignis für Meldung {ReportId} mit {StatusCode} ab (Versuch {Attempt}).", report.Id, (int)response.StatusCode, attempt);
                if (!retryable) return;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                logger.LogWarning("Benachrichtigungen ist nicht erreichbar ({ErrorType}, Versuch {Attempt}) für Meldung {ReportId}.", ex.GetType().Name, attempt, report.Id);
            }

            if (attempt < Attempts) await Task.Delay(RetryDelay, ct);
        }

        logger.LogError("Die Benachrichtigung für Meldung {ReportId} konnte nicht zugestellt werden.", report.Id);
    }
}
