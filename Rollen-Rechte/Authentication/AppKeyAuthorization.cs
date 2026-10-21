using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using Rollen_Rechte.Common.Api;

namespace Rollen_Rechte.Authentication;

/// <summary>
/// Section "Apps": Die anderen Anwendungen (Portal, HR-Tool, ...) melden beim Start ihren Rechtekatalog an. Je Anwendung ein eigener API-Schlüssel:
/// <c>Apps__Keys__portal=…</c>. Er gilt nur für die Registrierung der eigenen Rechte (<c>PUT /api/apps/{appKey}/catalog</c>).
/// </summary>
public class AppsOptions
{
    public const string SectionName = "Apps";

    public Dictionary<string, string> Keys { get; set; } = [];
}

/// <summary>
/// Verlangt für die Route <c>{appKey}</c> den passenden API-Schlüssel im Header <c>X-Api-Key</c> (zeitkonstanter Vergleich). Wird zusammen mit
/// <c>[AllowAnonymous]</c> verwendet: Diese Routen sind für Anwendungen, nicht für angemeldete Benutzer, und stehen deshalb außerhalb der JWT-Anmeldung.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AppKeyAttribute : Attribute, IAsyncAuthorizationFilter
{
    public const string HeaderName = "X-Api-Key";

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var appKey = context.RouteData.Values["appKey"]?.ToString();
        var configured = context.HttpContext.RequestServices.GetRequiredService<IOptions<AppsOptions>>().Value.Keys;
        var supplied = context.HttpContext.Request.Headers[HeaderName].ToString();

        var valid = !string.IsNullOrEmpty(appKey) && !string.IsNullOrEmpty(supplied)
                    && configured.TryGetValue(appKey, out var expected) && !string.IsNullOrEmpty(expected)
                    && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(expected));

        if (!valid)
            context.Result = new ObjectResult(new ErrorResponse("API-Schlüssel fehlt oder ist ungültig.", []))
            {
                StatusCode = StatusCodes.Status401Unauthorized
            };
        return Task.CompletedTask;
    }
}
