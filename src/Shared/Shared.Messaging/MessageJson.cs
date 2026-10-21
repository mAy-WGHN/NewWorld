using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace Shared.Messaging;

/// <summary>
/// The one wire format of all messages: camelCase, ISO 8601 dates, enums as strings, readable umlauts, unknown fields ignored
/// (forward compatibility). Unknown enum texts fail; consumers must not guess.
/// </summary>
public static class MessageJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static string Serialize(object message) => JsonSerializer.Serialize(message, message.GetType(), Options);

    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException($"Message body is empty ({typeof(T).Name}).");

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
            // Umlauts stay readable; HTML-sensitive characters are still escaped.
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
