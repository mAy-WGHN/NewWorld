using System.Net;
using System.Net.Http.Json;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using BcIntegration.Core.Errors;
using BcIntegration.Core.MasterData;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace BcIntegration.Infrastructure.BusinessCentral;

/// <summary>
/// The only BC client in the system. Reads master data (filtered on <c>lastModifiedDateTime</c>, following
/// <c>@odata.nextLink</c>) and writes absences as upsert via their external ID. Retries, throttling, timeouts and the
/// token are handled by the HTTP pipeline; this class translates BC answers into results or exceptions without
/// passing on any content of BC answers.
/// </summary>
public sealed class BcClient(HttpClient http, IOptions<BcClientOptions> options, TimeProvider time) : IBcMasterDataSource
{
    /// <summary>Codeunit and function of the PIN check: the same SOAP call the HR tool and the archived BC-Anbindung make.</summary>
    private const string PinCodeunit = "Zeiterfassung";
    private const string PinFunction = "GetStatus";

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly BcClientOptions _options = options.Value;

    public async Task<IReadOnlyList<BcEmployee>> GetEmployeesModifiedSinceAsync(DateTimeOffset? since, CancellationToken cancellationToken)
    {
        var employees = await GetAllAsync<BcEmployeeDto>(Path(_options.EmployeesPath, "EmployeesPath"), since, BcPriority.Normal, cancellationToken);
        return employees.Select(e => new BcEmployee(
            e.Id, e.Number, e.DisplayName, Blank(e.StoreNumber), Blank(e.PositionCode), Blank(e.EmploymentType),
            e.ContractedHoursPerWeek, NoDate(e.EmploymentDate), NoDate(e.TerminationDate), e.LastModifiedDateTime)).ToList();
    }

    public async Task<IReadOnlyList<BcStore>> GetStoresModifiedSinceAsync(DateTimeOffset? since, CancellationToken cancellationToken)
    {
        var stores = await GetAllAsync<BcStoreDto>(Path(_options.StoresPath, "StoresPath"), since, BcPriority.Normal, cancellationToken);
        return stores.Select(s => new BcStore(
            s.Id, s.Number, s.Name,
            s.Address is null || s.PostalCode is null || s.City is null ? null : new Address(s.Address, s.PostalCode, s.City),
            [], // TODO(BC-Modell): opening hours source
            IsActive: s.Closed != true,
            s.LastModifiedDateTime)).ToList();
    }

    /// <summary>Looks up an absence by external ID; <c>null</c> if BC does not know it yet.</summary>
    internal async Task<BcAbsenceDto?> FindAbsenceAsync(Guid externalId, CancellationToken cancellationToken)
    {
        var url = $"{Path(_options.AbsencesPath, "AbsencesPath")}?$filter=externalId eq {externalId}";
        var page = await SendAsync<ODataPage<BcAbsenceDto>>(new HttpRequestMessage(HttpMethod.Get, url), BcPriority.High, cancellationToken);
        return page.Value.Count > 0 ? page.Value[0] : null;
    }

    /// <summary>Creates or updates (with ETag) an absence. Business rejections are returned as error code.</summary>
    internal async Task<(BcAbsenceDto? Written, BcError? Rejected)> WriteAbsenceAsync(
        BcAbsenceDto? existing, BcAbsenceWriteDto body, CancellationToken cancellationToken)
    {
        var path = Path(_options.AbsencesPath, "AbsencesPath");
        var request = existing is null
            ? new HttpRequestMessage(HttpMethod.Post, path)
            : new HttpRequestMessage(HttpMethod.Patch, $"{path}({existing.Id})");
        request.Content = JsonContent.Create(body, options: Json);
        if (existing?.ETag is { } etag)
        {
            request.Headers.TryAddWithoutValidation("If-Match", etag);
        }

        try
        {
            return (await SendAsync<BcAbsenceDto>(request, BcPriority.High, cancellationToken), null);
        }
        catch (BcRejectedException rejected)
        {
            return (null, rejected.Error);
        }
    }

    /// <summary>
    /// Asks BC to compare the PIN: SOAP call of <c>Zeiterfassung.GetStatus(employeeNo, queryDate, password)</c>, as the HR tool does. BC answers
    /// <c>{"success":bool,"employeeName":...}</c> inside <c>return_value</c>; wrong PIN and unknown number are <c>success:false</c> (HTTP 200).
    /// The request is write-like: it is not repeated on unclear outcomes. The PIN is only in this request and never in an exception or log.
    /// </summary>
    internal async Task<BcPinVerifyResponse> VerifyPinAsync(string personnelNumber, string pin, CancellationToken cancellationToken)
    {
        var url = SoapUrl(PinCodeunit);
        var queryDate = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var ns = $"urn:microsoft-dynamics-schemas/codeunit/{PinCodeunit}";
        var envelope =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>"
            + "<soap:Envelope xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\"><soap:Body>"
            + $"<{PinFunction} xmlns=\"{ns}\">"
            + $"<employeeNo>{SecurityElement.Escape(personnelNumber)}</employeeNo>"
            + $"<queryDate>{queryDate}</queryDate>"
            + $"<password>{SecurityElement.Escape(pin)}</password>"
            + $"</{PinFunction}></soap:Body></soap:Envelope>";
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(envelope, Encoding.UTF8, "text/xml") };
        request.Headers.TryAddWithoutValidation("SOAPAction", $"\"{ns}:{PinFunction}\"");
        try
        {
            return await SendAsync(request, BcPriority.High, ReadPinAnswerAsync, cancellationToken);
        }
        catch (BcRejectedException)
        {
            // A business rejection of the request itself (not of the PIN) means the call does not match BC.
            throw new BcTechnicalFailureException("BcPinRequestRejected");
        }
    }

    /// <summary>Reads <c>return_value</c> of the SOAP answer. No DTD processing; neither fault text nor body is passed on.</summary>
    private static async Task<BcPinVerifyResponse> ReadPinAnswerAsync(HttpContent content, CancellationToken cancellationToken)
    {
        var xml = await content.ReadAsStringAsync(cancellationToken);
        string? returnValue;
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var document = XDocument.Load(reader);
            if (document.Descendants().Any(e => e.Name.LocalName == "Fault"))
            {
                throw new BcTechnicalFailureException("BcSoapFault");
            }

            returnValue = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "return_value")?.Value;
        }
        catch (XmlException)
        {
            throw new BcTechnicalFailureException("BcInvalidResponse");
        }

        if (string.IsNullOrWhiteSpace(returnValue))
        {
            throw new BcTechnicalFailureException("BcEmptyResponse");
        }

        try
        {
            var status = JsonSerializer.Deserialize<BcGetStatusDto>(returnValue, Json) ?? throw new BcTechnicalFailureException("BcEmptyResponse");
            return new BcPinVerifyResponse(status.Success, status.EmployeeName);
        }
        catch (JsonException)
        {
            throw new BcTechnicalFailureException("BcInvalidResponse");
        }
    }

    private async Task<List<T>> GetAllAsync<T>(string path, DateTimeOffset? since, BcPriority priority, CancellationToken cancellationToken)
    {
        var items = new List<T>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        Uri? next = new(
            since is null ? path : $"{path}?$filter=lastModifiedDateTime gt {since.Value.UtcDateTime:yyyy-MM-ddTHH:mm:ss.fffZ}",
            UriKind.Relative);
        while (next is not null)
        {
            var absolute = new Uri(http.BaseAddress ?? new Uri("http://bc/"), next).AbsoluteUri;
            if (!visited.Add(absolute))
            {
                throw new BcTechnicalFailureException("BcPaginationLoop");
            }

            var page = await SendAsync<ODataPage<T>>(new HttpRequestMessage(HttpMethod.Get, next), priority, cancellationToken);
            items.AddRange(page.Value);
            next = page.NextLink;
        }

        return items;
    }

    private Task<T> SendAsync<T>(HttpRequestMessage request, BcPriority priority, CancellationToken cancellationToken) =>
        SendAsync(request, priority, ReadJsonAsync<T>, cancellationToken);

    private static async Task<T> ReadJsonAsync<T>(HttpContent content, CancellationToken cancellationToken)
    {
        try
        {
            return await content.ReadFromJsonAsync<T>(Json, cancellationToken) ?? throw new BcTechnicalFailureException("BcEmptyResponse");
        }
        catch (JsonException)
        {
            throw new BcTechnicalFailureException("BcInvalidResponse");
        }
    }

    private async Task<T> SendAsync<T>(
        HttpRequestMessage request, BcPriority priority, Func<HttpContent, CancellationToken, Task<T>> read, CancellationToken cancellationToken)
    {
        using (request)
        {
            request.Options.Set(BcRateLimitingHandler.PriorityKey, priority);
            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(request, cancellationToken);
            }
            catch (Exception ex) when (ex is TimeoutRejectedException or BrokenCircuitException or HttpRequestException
                                           || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                throw new BcTransientFailureException(ex is TimeoutRejectedException or TaskCanceledException ? "Timeout" : ex.GetType().Name);
            }

            using (response)
            {
                if (response.IsSuccessStatusCode)
                {
                    return await read(response.Content, cancellationToken);
                }

                var error = BcErrorClassifier.Classify((int)response.StatusCode, await ReadErrorCodeAsync(response, cancellationToken));
                throw error.Kind switch
                {
                    BcErrorKind.Transient => new BcTransientFailureException($"Http{(int)response.StatusCode}"),
                    BcErrorKind.Business when request.Method != HttpMethod.Get => new BcRejectedException(error),
                    BcErrorKind.Business => new BcTechnicalFailureException($"BcQueryRejected{(int)response.StatusCode}"),
                    _ => new BcTechnicalFailureException(error.TechnicalReason ?? $"Http{(int)response.StatusCode}"),
                };
            }
        }
    }

    private static async Task<string?> ReadErrorCodeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return (await response.Content.ReadFromJsonAsync<BcErrorDto>(Json, cancellationToken))?.Error?.Code;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // Not a JSON error body (e.g. a SOAP fault): only the status is used.
            return null;
        }
    }

    private Uri SoapUrl(string codeunit)
    {
        if (_options.SoapBaseUrl is null)
        {
            throw new BcTechnicalFailureException("BcNotConfigured:SoapBaseUrl");
        }

        if (string.IsNullOrWhiteSpace(_options.CompanyName))
        {
            throw new BcTechnicalFailureException("BcNotConfigured:CompanyName");
        }

        var root = _options.SoapBaseUrl.AbsoluteUri;
        return new Uri((root.EndsWith('/') ? root : root + "/") + $"{Uri.EscapeDataString(_options.CompanyName)}/Codeunit/{codeunit}");
    }

    private string Path(string template, string setting) =>
        string.IsNullOrWhiteSpace(template)
            ? throw new BcTechnicalFailureException($"BcNotConfigured:{setting}")
            : template.Replace("{companyId}", _options.CompanyId, StringComparison.Ordinal);

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>BC returns 0001-01-01 for empty dates.</summary>
    private static DateOnly? NoDate(DateOnly? value) => value == DateOnly.MinValue ? null : value;

    /// <summary>BC answered a write with a business rejection (no retry).</summary>
    private sealed class BcRejectedException(BcError error) : Exception("BC rejected the write.")
    {
        public BcError Error { get; } = error;
    }
}
