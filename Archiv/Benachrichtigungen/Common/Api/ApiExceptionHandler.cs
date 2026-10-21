using Microsoft.AspNetCore.Diagnostics;

namespace Benachrichtigungen.Common.Api;

public class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger, IHostEnvironment env) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        ErrorResponse body;
        int status;

        if (exception is ApiException api)
        {
            status = api.StatusCode;
            body = new ErrorResponse(api.Message, api.Errors);
        }
        else
        {
            logger.LogError(exception, "Unerwarteter Fehler bei {Method} {Path}", context.Request.Method, context.Request.Path);
            status = StatusCodes.Status500InternalServerError;
            body = new ErrorResponse(
                env.IsDevelopment()
                    ? $"{exception.GetType().Name}: {exception.Message}"
                    : "Es ist ein interner Fehler aufgetreten.",
                []);
        }

        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(body, ct);
        return true;
    }
}

public static class ApiErrorWriter
{
    /// <summary>Schreibt einen Fehler-Envelope für Antworten außerhalb von MVC (Status-Pages, Rate-Limit).</summary>
    public static Task WriteAsync(HttpContext context, int status, string message, CancellationToken ct = default)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new ErrorResponse(message, []), ct);
    }
}
