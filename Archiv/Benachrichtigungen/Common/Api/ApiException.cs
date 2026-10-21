namespace Benachrichtigungen.Common.Api;

public record ApiError(string Field, string Message);

/// <summary>Fachlicher Fehler mit Statuscode; wird vom ApiExceptionHandler in den Fehler-Envelope übersetzt.</summary>
public class ApiException(int statusCode, string message, IReadOnlyList<ApiError>? errors = null) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public IReadOnlyList<ApiError> Errors { get; } = errors ?? [];

    public const string NotFoundMessage =
        "Der angeforderte Datensatz wurde nicht gefunden — möglicherweise wurde er in der Zwischenzeit gelöscht.";

    public static ApiException NotFound(string? message = null) => new(404, message ?? NotFoundMessage);
    public static ApiException BadRequest(string message) => new(400, message);
    public static ApiException Conflict(string message) => new(409, message);

    public static ApiException Validation(string field, string message) =>
        new(422, "Validierung fehlgeschlagen.", [new ApiError(field, message)]);
}
