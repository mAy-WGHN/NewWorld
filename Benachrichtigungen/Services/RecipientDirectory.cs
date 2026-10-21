using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Benachrichtigungen.Services;

/// <summary>Section "Access": Adresse des Dienstes "Rollen-Rechte" und der Schlüssel, den er für diese Anwendung vergeben hat (dort: <c>Apps__Keys__{AppKey}</c>).</summary>
public class AccessOptions
{
    public const string SectionName = "Access";

    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Name dieser Anwendung bei "Rollen-Rechte" (Teil der Adresse und des Schlüssels).</summary>
    public string AppKey { get; set; } = "notifications";

    public string ApiKey { get; set; } = string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(AppKey);
}

/// <summary>"Rollen-Rechte" ist nicht erreichbar, antwortet mit einem Fehler oder ihre Antwort ist unbrauchbar. Das Ereignis wird dann nicht als verarbeitet markiert (der Absender wiederholt es).</summary>
public class RecipientDirectoryUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Wer darf einen Vorgang sehen? Die Antwort kommt vom Dienst "Rollen-Rechte" (Rechte, Scopes, Gültigkeit, aktiv).</summary>
public interface IRecipientDirectory
{
    /// <summary>Die User aus <paramref name="candidateUserIds"/>, die <paramref name="privilege"/> in Bezug auf diese Filialen/Einzelobjekte haben ("eine Filiale genügt").</summary>
    Task<IReadOnlyCollection<Guid>> FindUsersAsync(string privilege, IReadOnlyCollection<Guid> locationIds, IReadOnlyCollection<Guid> objectIds,
        IReadOnlyCollection<Guid> candidateUserIds, CancellationToken ct);
}

public class HttpRecipientDirectory(IHttpClientFactory httpClientFactory, IOptions<AccessOptions> options, ILogger<HttpRecipientDirectory> logger) : IRecipientDirectory
{
    public const string HttpClientName = "Access";
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(15);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyCollection<Guid>> FindUsersAsync(string privilege, IReadOnlyCollection<Guid> locationIds, IReadOnlyCollection<Guid> objectIds,
        IReadOnlyCollection<Guid> candidateUserIds, CancellationToken ct)
    {
        var o = options.Value;
        if (!o.IsConfigured) throw new RecipientDirectoryUnavailableException("Der Zugang zu Rollen-Rechte ist nicht konfiguriert.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(CallTimeout);
        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post,
                new Uri(new Uri(o.BaseUrl.TrimEnd('/') + "/"), $"api/apps/{Uri.EscapeDataString(o.AppKey)}/recipients"))
            {
                Content = JsonContent.Create(new { privilege, locationIds, reportIds = objectIds, userIds = candidateUserIds })
            };
            request.Headers.Add("X-Api-Key", o.ApiKey);

            using var response = await client.SendAsync(request, timeout.Token);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                logger.LogWarning("Rollen-Rechte antwortete auf die Empfängerfrage mit {StatusCode}.", (int)response.StatusCode);
                throw new RecipientDirectoryUnavailableException($"Rollen-Rechte antwortete mit {(int)response.StatusCode}.");
            }

            var envelope = await response.Content.ReadFromJsonAsync<Envelope>(Json, timeout.Token);
            return envelope is { Success: true, Data: not null }
                ? envelope.Data.UserIds
                : throw new RecipientDirectoryUnavailableException("Die Antwort von Rollen-Rechte ist unbrauchbar.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Rollen-Rechte ist nicht erreichbar ({ErrorType}).", ex.GetType().Name);
            throw new RecipientDirectoryUnavailableException("Rollen-Rechte ist nicht erreichbar.", ex);
        }
    }

    private sealed record Envelope(bool Success, Payload? Data);

    private sealed record Payload(List<Guid> UserIds);
}
