using System.Text.Json.Serialization;

namespace BcIntegration.Infrastructure.BusinessCentral;

/// <summary>OData collection page (<c>value</c> plus <c>@odata.nextLink</c> for pagination).</summary>
internal sealed record ODataPage<T>(
    [property: JsonPropertyName("value")] IReadOnlyList<T> Value,
    [property: JsonPropertyName("@odata.nextLink")] Uri? NextLink);

/// <summary>
/// Employee of the BC API. Standard API v2.0 fields: id, number, displayName, employmentDate, terminationDate,
/// lastModifiedDateTime. TODO(BC-Modell): storeNumber, positionCode, employmentType, contractedHoursPerWeek are assumed
/// fields of a custom API page; the standard API does not provide them.
/// </summary>
internal sealed record BcEmployeeDto(
    Guid Id,
    string Number,
    string DisplayName,
    DateOnly? EmploymentDate,
    DateOnly? TerminationDate,
    DateTimeOffset LastModifiedDateTime,
    string? StoreNumber,
    string? PositionCode,
    string? EmploymentType,
    decimal? ContractedHoursPerWeek);

/// <summary>LSC store. TODO(BC-Modell): all fields assumed (custom API page); opening hours not yet mapped.</summary>
internal sealed record BcStoreDto(
    Guid Id,
    string Number,
    string Name,
    string? Address,
    string? PostalCode,
    string? City,
    bool? Closed,
    DateTimeOffset LastModifiedDateTime);

/// <summary>Employee absence. TODO(BC-Modell): all fields assumed (custom API page with external ID and version).</summary>
internal sealed record BcAbsenceDto(
    Guid Id,
    [property: JsonPropertyName("@odata.etag")] string? ETag,
    Guid ExternalId,
    string EmployeeNumber,
    DateOnly FromDate,
    DateOnly? ToDate,
    string CauseOfAbsenceCode,
    string Status,
    long ExternalVersion);

/// <summary>Body of a write; same assumptions as <see cref="BcAbsenceDto"/>.</summary>
internal sealed record BcAbsenceWriteDto(
    Guid ExternalId,
    string EmployeeNumber,
    DateOnly FromDate,
    DateOnly? ToDate,
    string CauseOfAbsenceCode,
    string Status,
    long ExternalVersion);

/// <summary>BC error body <c>{"error":{"code":...,"message":...}}</c>. Only the code is read; the message may contain data.</summary>
internal sealed record BcErrorDto([property: JsonPropertyName("error")] BcErrorDetail? Error);

internal sealed record BcErrorDetail([property: JsonPropertyName("code")] string? Code);

/// <summary>
/// Body of the PIN check. TODO(BC-Modell): the archived BC-Anbindung calls the SOAP codeunit Zeiterfassung.GetStatus; a REST/OData
/// action with this shape is assumed here. The PIN is only ever in this request body.
/// </summary>
internal sealed record BcPinVerifyRequest(string EmployeeNumber, string Pin);

internal sealed record BcPinVerifyResponse(bool Authenticated, string? EmployeeName);
