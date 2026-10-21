using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using BC_Anbindung.Authentication;
using BC_Anbindung.Common.Api;
using BC_Anbindung.Contracts;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace BC_Anbindung.BusinessCentral;

/// <summary>
/// Die fachlichen Abfragen an Business Central, die die anderen Programme brauchen. Hier — und nur hier — kennt jemand die
/// BC-Codeunits und ihre Eigenheiten; die Aufrufer bekommen einfache Ergebnisse.
///
/// Fehlerverhalten: BC technisch nicht erreichbar (Azure AD, Netzwerk, Timeout, unlesbare Antwort) → <see cref="ApiException"/> mit 503,
/// damit der Aufrufer zwischen "BC sagt nein" und "BC ist weg" unterscheiden kann. Meldungen und Details enthalten nie den SOAP-Body
/// (er kann das PIN enthalten); geloggt wird nur der Fehlertyp.
/// </summary>
public class BcGateway(IBcCodeunitClient bc, IOptions<BcAuthOptions> options, IOptions<GatewayOptions> gateway, IMemoryCache cache, ILogger<BcGateway> logger)
{
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(10);
    private const string StoresCacheKey = "bc-stores";

    /// <summary>
    /// Prüft Personalnummer und PIN über die Codeunit "Zeiterfassung" (Funktion "GetStatus"): BC vergleicht das PIN selbst und antwortet nur mit
    /// Erfolg/Misserfolg und dem Namen. Unbekannte Personalnummer und falsches PIN liefern sauberes "success:false" (HTTP 200); eine Exception ist
    /// deshalb ein technischer Fehler. Ein LEERES PIN prüft BC nicht und liefert "success:true" — es wird nie an BC gegeben.
    /// </summary>
    public async Task<VerifyEmployeeResponse> VerifyEmployeeAsync(string employeeNo, string pin, CancellationToken ct)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(employeeNo) || string.IsNullOrWhiteSpace(pin)) return new VerifyEmployeeResponse(false, null);

        GetStatusResponse? status;
        try
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var json = await CallAsync("Zeiterfassung", "GetStatus",
                [("employeeNo", employeeNo), ("queryDate", today), ("password", pin)], ct);
            status = JsonSerializer.Deserialize<GetStatusResponse>(json);
        }
        catch (Exception ex) when (IsTechnical(ex, ct))
        {
            throw Unavailable(ex);
        }

        var authenticated = status is { Success: true } && !string.IsNullOrWhiteSpace(status.EmployeeName);
        return new VerifyEmployeeResponse(authenticated, authenticated ? status!.EmployeeName : null);
    }

    /// <summary>Alle Filialen (LS Central "LSC Store" über die Codeunit "GetStores"), kurz zwischengespeichert (<see cref="GatewayOptions.StoresCacheSeconds"/>).</summary>
    public async Task<IReadOnlyList<StoreResponse>> GetStoresAsync(CancellationToken ct)
    {
        EnsureConfigured();
        if (cache.TryGetValue(StoresCacheKey, out IReadOnlyList<StoreResponse>? cached) && cached is not null) return cached;

        try
        {
            // storeGetType=1 liefert alle Filialen (live in der Inventur-API ermittelt).
            var json = await CallAsync("GetStores", "GetStores",
                [("storeGetType", "1"), ("searchText", ""), ("includeDetail", "true")], ct);
            var stores = LscCodeunitResponse.GetDataSetRows(json, "LSC Store")
                .Select(row => new StoreResponse(row.GetValueOrDefault("No.")?.Trim() ?? "", row.GetValueOrDefault("Name")?.Trim() ?? ""))
                .Where(s => s.StoreNo.Length > 0 && s.Name.Length > 0)
                .OrderBy(s => s.StoreNo, StringComparer.Ordinal)
                .ToList();

            var seconds = gateway.Value.StoresCacheSeconds;
            if (seconds > 0) cache.Set(StoresCacheKey, (IReadOnlyList<StoreResponse>)stores, TimeSpan.FromSeconds(seconds));
            return stores;
        }
        catch (Exception ex) when (IsTechnical(ex, ct))
        {
            throw Unavailable(ex);
        }
    }

    private async Task<string> CallAsync(string codeunit, string operation, IEnumerable<(string Name, string Value)> parameters, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(CallTimeout);
        return await bc.CallAsync(codeunit, operation, parameters, timeout.Token);
    }

    private void EnsureConfigured()
    {
        if (!options.Value.IsConfigured)
            throw new ApiException(StatusCodes.Status503ServiceUnavailable, "Die Anbindung an Business Central ist nicht konfiguriert.");
    }

    private static bool IsTechnical(Exception ex, CancellationToken ct) =>
        ex is BcAuthException or HttpRequestException or JsonException or InvalidOperationException or TaskCanceledException
        && !ct.IsCancellationRequested;

    private ApiException Unavailable(Exception ex)
    {
        // Bewusst nur der Typ: Meldungen/Details könnten den SOAP-Body samt PIN enthalten.
        logger.LogWarning("Business Central nicht erreichbar oder Antwort unbrauchbar ({ErrorType}).", ex.GetType().Name);
        return new ApiException(StatusCodes.Status503ServiceUnavailable, "Business Central ist nicht erreichbar.");
    }

    private sealed record GetStatusResponse(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("employeeName")] string? EmployeeName);
}
