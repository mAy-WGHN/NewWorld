using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rollen_Rechte.Serialization;

/// <summary>Zentrale JSON-Einstellungen: camelCase, Lowercase-Enums, ISO-8601-Zeitstempel mit Offset.</summary>
public static class ApiJson
{
    public static void Configure(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        // Reine JSON-API (kein HTML-Kontext): "+" und Umlaute bleiben lesbar statt als Unicode-Escape.
        options.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new Iso8601DateTimeConverter());
    }
}

/// <summary>
/// Schreibt UTC-Werte als "2026-10-12T14:03:22+00:00".
/// Beim Lesen wird ein mitgeschickter Offset bewusst NICHT umgerechnet: Wie im Symfony-Original landet die
/// Wanduhrzeit des Clients unverändert in der Datenbank ("10:00+02:00" wird als 10:00 gespeichert).
/// Nicht "verbessern", sonst weichen alte und neue Daten voneinander ab.
/// </summary>
public class Iso8601DateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (!DateTimeOffset.TryParse(reader.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var value))
            throw new JsonException("Ungültiges Datumsformat.");
        return DateTime.SpecifyKind(value.DateTime, DateTimeKind.Utc);
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        var utc = value.Kind == DateTimeKind.Local
            ? value.ToUniversalTime()
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        writer.WriteStringValue(new DateTimeOffset(utc).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture));
    }
}
