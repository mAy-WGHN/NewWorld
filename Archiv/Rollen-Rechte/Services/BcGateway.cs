using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Rollen_Rechte.Services;

/// <summary>Section "BcGateway": Adresse und API-Schlüssel des Dienstes "BC-Anbindung" (der einzige Weg zu Business Central).</summary>
public class BcGatewayOptions
{
    public const string SectionName = "BcGateway";

    /// <summary>Basisadresse des Dienstes, z. B. <c>http://localhost:5301</c>.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Der Schlüssel, den die BC-Anbindung für diesen Aufrufer ("Identity") vergeben hat (<c>Gateway__ApiKeys__Identity</c> dort).</summary>
    public string ApiKey { get; set; } = string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(ApiKey);
}

public record BcEmployeeCheck(bool Authenticated, string? EmployeeName);

public record BcStore(string StoreNo, string Name);

/// <summary>Die BC-Anbindung ist nicht erreichbar, antwortet mit 503/Fehler oder ihre Antwort ist unbrauchbar. Enthält nie Inhalte der Anfrage (PIN).</summary>
public class BcGatewayUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Der Zugang zu Business Central aus Sicht dieses Dienstes: einfache Fragen an den Dienst "BC-Anbindung" statt SOAP.</summary>
public interface IBcGateway
{
    bool IsConfigured { get; }

    /// <summary>Stimmen Personalnummer und PIN? <c>Authenticated = false</c> heißt: BC kennt sie nicht. Technische Fehler: <see cref="BcGatewayUnavailableException"/>.</summary>
    Task<BcEmployeeCheck> VerifyEmployeeAsync(string employeeNo, string pin, CancellationToken ct);

    Task<IReadOnlyList<BcStore>> GetStoresAsync(CancellationToken ct);
}

public class HttpBcGateway(IHttpClientFactory httpClientFactory, IOptions<BcGatewayOptions> options, ILogger<HttpBcGateway> logger) : IBcGateway
{
    public const string HttpClientName = "BcGateway";
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(15);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public bool IsConfigured => options.Value.IsConfigured;

    public async Task<BcEmployeeCheck> VerifyEmployeeAsync(string employeeNo, string pin, CancellationToken ct)
    {
        var data = await SendAsync<BcEmployeeCheck>(HttpMethod.Post, "api/employees/verify", new { employeeNo, pin }, ct);
        return data;
    }

    public async Task<IReadOnlyList<BcStore>> GetStoresAsync(CancellationToken ct) =>
        await SendAsync<List<BcStore>>(HttpMethod.Get, "api/stores", null, ct);

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        if (!IsConfigured) throw new BcGatewayUnavailableException("Die BC-Anbindung ist nicht konfiguriert.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(CallTimeout);
        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(method, new Uri(new Uri(options.Value.BaseUrl.TrimEnd('/') + "/"), path));
            request.Headers.Add("X-Api-Key", options.Value.ApiKey);
            if (body is not null) request.Content = JsonContent.Create(body);

            using var response = await client.SendAsync(request, timeout.Token);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                // Bewusst nur der Statuscode: Der Body gehört der BC-Anbindung und enthält nie das PIN, aber wir geben ohnehin nichts davon weiter.
                logger.LogWarning("Die BC-Anbindung antwortete mit {StatusCode}.", (int)response.StatusCode);
                throw new BcGatewayUnavailableException($"Die BC-Anbindung antwortete mit {(int)response.StatusCode}.");
            }

            var envelope = await response.Content.ReadFromJsonAsync<Envelope<T>>(Json, timeout.Token);
            return envelope is { Success: true, Data: not null }
                ? envelope.Data
                : throw new BcGatewayUnavailableException("Die Antwort der BC-Anbindung ist unbrauchbar.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Die BC-Anbindung ist nicht erreichbar ({ErrorType}).", ex.GetType().Name);
            throw new BcGatewayUnavailableException("Die BC-Anbindung ist nicht erreichbar.", ex);
        }
    }

    private sealed record Envelope<T>(bool Success, T? Data);
}
