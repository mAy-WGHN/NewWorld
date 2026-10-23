using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
public sealed class BcClient(HttpClient http, IOptions<BcClientOptions> options) : IBcMasterDataSource
{
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

    private async Task<T> SendAsync<T>(HttpRequestMessage request, BcPriority priority, CancellationToken cancellationToken)
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
                    try
                    {
                        return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken)
                            ?? throw new BcTechnicalFailureException("BcEmptyResponse");
                    }
                    catch (JsonException)
                    {
                        throw new BcTechnicalFailureException("BcInvalidResponse");
                    }
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
        catch (JsonException)
        {
            return null;
        }
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
