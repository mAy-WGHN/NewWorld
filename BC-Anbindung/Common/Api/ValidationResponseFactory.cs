using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace BC_Anbindung.Common.Api;

public static class ValidationResponseFactory
{
    /// <summary>Wandelt ModelState-Fehler in 422 mit errors[{field, message}] um.</summary>
    public static IActionResult Create(ActionContext context)
    {
        var errors = new List<ApiError>();
        foreach (var (key, entry) in context.ModelState)
        {
            if (entry.ValidationState != ModelValidationState.Invalid) continue;
            var field = NormalizeField(key);
            foreach (var error in entry.Errors)
            {
                errors.Add(new ApiError(field, Localize(error.ErrorMessage)));
            }
        }

        return new ObjectResult(new ErrorResponse("Validierung fehlgeschlagen.", errors))
        {
            StatusCode = StatusCodes.Status422UnprocessableEntity
        };
    }

    // Eigene Attribute liefern deutsche Texte; Meldungen des JSON-Parsers/Model-Binders sind englisch
    // und enthalten technische Details (Pfad, Zeile) — die ersetzen wir durch einen neutralen Text.
    private static string Localize(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "Der Wert ist ungültig.";
        if (message.StartsWith("A non-empty request body")) return "Der Request-Body fehlt oder ist ungültig.";
        if (message.StartsWith("The ") || message.Contains("Path: $")) return "Der Wert ist ungültig.";
        return message;
    }

    // "Keys.P256dh" -> "keys.p256dh"; "$.category" -> "category"; "$" bleibt leer.
    private static string NormalizeField(string key)
    {
        if (key.StartsWith("$.")) key = key[2..];
        else if (key == "$") key = "";

        return string.Join('.', key.Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => char.ToLowerInvariant(s[0]) + s[1..]));
    }
}
