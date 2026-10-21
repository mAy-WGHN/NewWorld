using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HrTool_API.Serialization;

/// <summary>Eine einzige, benannte JSON-Konfiguration für diese API (eingehend wie ausgehend): camelCase, camelCase-Enums.</summary>
public static class HrToolJsonOptions
{
    public static void Configure(JsonSerializerOptions options)
    {
        options.PropertyNameCaseInsensitive = true;
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        // Reine JSON-API (kein HTML-Kontext): "+" und Umlaute bleiben lesbar statt als Unicode-Escape.
        options.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
        options.NumberHandling = JsonNumberHandling.AllowReadingFromString;
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }
}
