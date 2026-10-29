using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.RateLimiting;
using Shared.Authorization;

namespace Portal.Api.Http;

/// <summary>The answer format the existing frontend expects: <c>{success:true,data}</c> and <c>{success:false,message,errors[{field,message}]}</c>.</summary>
public sealed record SuccessResponse<T>(T Data)
{
    public bool Success => true;
}

public sealed record ApiError(string Field, string Message);

public sealed record ErrorResponse(string Message, IReadOnlyList<ApiError> Errors)
{
    public bool Success => false;
}

/// <summary>A business error with a status code; the handler turns it into the error envelope.</summary>
public sealed class ApiException(int statusCode, string message, IReadOnlyList<ApiError>? errors = null) : Exception(message)
{
    public const string NotFoundMessage = "Der angeforderte Datensatz wurde nicht gefunden — möglicherweise wurde er in der Zwischenzeit gelöscht.";

    public int StatusCode { get; } = statusCode;

    public IReadOnlyList<ApiError> Errors { get; } = errors ?? [];

    public static ApiException NotFound(string? message = null) => new(404, message ?? NotFoundMessage);

    public static ApiException BadRequest(string message) => new(400, message);

    public static ApiException Conflict(string message) => new(409, message);

    public static ApiException Validation(string field, string message) => new(422, "Validierung fehlgeschlagen.", [new ApiError(field, message)]);

    public static ApiException Validation(IReadOnlyList<ApiError> errors) => new(422, "Validierung fehlgeschlagen.", errors);
}

public static class Answer
{
    public static IResult Ok<T>(T data) => Results.Json(new SuccessResponse<T>(data));

    public static IResult Created<T>(T data) => Results.Json(new SuccessResponse<T>(data), statusCode: StatusCodes.Status201Created);

    public static IResult NoContent() => Results.NoContent();
}

/// <summary>Collects validation errors of one request (field in camelCase).</summary>
public sealed class Validator
{
    public const string Required = "Dieser Wert darf nicht leer sein.";

    private readonly List<ApiError> _errors = [];

    public void RequiredText(string field, string? value, int? maxLength = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            _errors.Add(new ApiError(field, Required));
        }
        else
        {
            MaxLength(field, value, maxLength);
        }
    }

    public void MaxLength(string field, string? value, int? maxLength)
    {
        if (maxLength is { } max && value is not null && value.Length > max)
        {
            _errors.Add(new ApiError(field, $"Dieser Wert ist zu lang. Er darf höchstens {max} Zeichen enthalten."));
        }
    }

    public void Add(string field, string message) => _errors.Add(new ApiError(field, message));

    public void ThrowIfInvalid()
    {
        if (_errors.Count > 0)
        {
            throw ApiException.Validation(_errors);
        }
    }
}

/// <summary>Turns exceptions into the error envelope. Unexpected errors are logged without content (type and path only).</summary>
public sealed partial class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger, IHostEnvironment environment) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        (int status, ErrorResponse body) = exception switch
        {
            ApiException api => (api.StatusCode, new ErrorResponse(api.Message, api.Errors)),
            BadHttpRequestException or JsonException => (StatusCodes.Status422UnprocessableEntity, new ErrorResponse("Validierung fehlgeschlagen.", [new ApiError("", "Der Request-Body fehlt oder ist ungültig.")])),
            InvalidDataException => (StatusCodes.Status422UnprocessableEntity, new ErrorResponse("Validierung fehlgeschlagen.", [new ApiError("file", "Die Datei ist zu groß.")])),
            _ => (StatusCodes.Status500InternalServerError, new ErrorResponse(environment.IsDevelopment() ? exception.GetType().Name : "Es ist ein interner Fehler aufgetreten.", [])),
        };
        if (status == StatusCodes.Status500InternalServerError)
        {
            LogUnexpected(logger, exception.GetType().Name, httpContext.Request.Method, httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(body, cancellationToken);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unexpected {ExceptionType} at {Method} {Path}")]
    private static partial void LogUnexpected(ILogger logger, string exceptionType, string method, string path);
}

/// <summary>401 only for "not signed in"; 403 with a fixed text per action. Both in the error envelope.</summary>
public sealed class EnvelopeAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Succeeded)
        {
            await next(context);
            return;
        }

        if (authorizeResult.Challenged)
        {
            await Write(context, StatusCodes.Status401Unauthorized, "Bitte melden Sie sich an.");
            return;
        }

        if (authorizeResult.Forbidden)
        {
            var key = authorizeResult.AuthorizationFailure?.FailedRequirements.OfType<PrivilegeRequirement>().FirstOrDefault()?.Key;
            await Write(context, StatusCodes.Status403Forbidden, Message(key?.Split(':').LastOrDefault()));
            return;
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }

    public static string Message(string? action) => action switch
    {
        "read" => "Sie haben nicht die erforderliche Berechtigung, diese Inhalte einzusehen.",
        "create" => "Sie haben nicht die erforderliche Berechtigung, diese Inhalte zu erstellen.",
        "update" => "Sie haben nicht die erforderliche Berechtigung, diese Inhalte zu bearbeiten.",
        "delete" => "Sie haben nicht die erforderliche Berechtigung, diese Inhalte zu löschen.",
        _ => "Sie haben nicht die erforderliche Berechtigung für diese Aktion.",
    };

    private static Task Write(HttpContext context, int status, string message)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new ErrorResponse(message, []));
    }
}

/// <summary>Sliding-window limits per client address (in memory, per instance). Reading and creating reports are public, so they are limited.</summary>
public sealed class RateLimitingOptions
{
    public const string ConfigurationSection = "Portal:RateLimiting";
    public const string ReportCreationPolicy = "report_creation";

    public int RequestsPerMinute { get; set; } = 100;

    public int ReportsPerMinute { get; set; } = 10;
}

public static class RateLimitingSetup
{
    public static IServiceCollection AddPortalRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var limits = configuration.GetSection(RateLimitingOptions.ConfigurationSection).Get<RateLimitingOptions>() ?? new RateLimitingOptions();
        return services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = System.Threading.RateLimiting.PartitionedRateLimiter.Create<HttpContext, string>(context =>
                HttpMethods.IsOptions(context.Request.Method)
                    ? System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("options")
                    : System.Threading.RateLimiting.RateLimitPartition.GetSlidingWindowLimiter(ClientAddress(context), _ => Window(limits.RequestsPerMinute)));
            options.AddPolicy(RateLimitingOptions.ReportCreationPolicy, context =>
                System.Threading.RateLimiting.RateLimitPartition.GetSlidingWindowLimiter(ClientAddress(context), _ => Window(limits.ReportsPerMinute)));
            options.OnRejected = async (context, cancellationToken) =>
            {
                var seconds = context.Lease.TryGetMetadata(System.Threading.RateLimiting.MetadataName.RetryAfter, out var retry) ? Math.Max(1, (int)Math.Ceiling(retry.TotalSeconds)) : 1;
                context.HttpContext.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await context.HttpContext.Response.WriteAsJsonAsync(new ErrorResponse("Zu viele Anfragen, bitte in Kürze erneut versuchen.", []), cancellationToken);
            };
        });
    }

    private static string ClientAddress(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static System.Threading.RateLimiting.SlidingWindowRateLimiterOptions Window(int limit) => new()
    {
        PermitLimit = limit,
        Window = TimeSpan.FromMinutes(1),
        SegmentsPerWindow = 6,
        QueueLimit = 0,
    };
}

public static class JsonSetup
{
    /// <summary>camelCase, enums as lower-case words (the frontend sends and expects "open", "theft" …).</summary>
    public static IServiceCollection AddPortalJson(this IServiceCollection services) =>
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        });
}
