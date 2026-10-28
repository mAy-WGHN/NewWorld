using System.Text.Json;

namespace Notifications.Core.Templates;

/// <summary>Reads a template file (JSON). Placeholders: <c>"name": "sensitive"</c> or <c>"public"</c>.</summary>
public static class TemplateParser
{
    public static Template Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var placeholders = new Dictionary<string, bool>(StringComparer.Ordinal);
            if (root.TryGetProperty("placeholders", out var declared))
            {
                foreach (var property in declared.EnumerateObject())
                {
                    placeholders[property.Name] = property.Value.GetString() switch
                    {
                        "sensitive" => true,
                        "public" => false,
                        var other => throw new TemplateException($"Placeholder '{property.Name}' must be 'sensitive' or 'public', not '{other}'."),
                    };
                }
            }

            return new Template(
                root.GetProperty("key").GetString() ?? "",
                root.TryGetProperty("mandatory", out var mandatory) && mandatory.GetBoolean(),
                placeholders,
                Text(root, "inApp"),
                Text(root, "push"),
                Text(root, "email"),
                root.TryGetProperty("sensitiveText", out var sensitiveText) && sensitiveText.GetBoolean(),
                root.TryGetProperty("mailMayContainSensitive", out var mailSensitive) && mailSensitive.GetBoolean());
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new TemplateException("Template file is not readable: " + ex.GetType().Name);
        }
    }

    private static TextPair Text(JsonElement root, string name)
    {
        var element = root.GetProperty(name);
        return new TextPair(element.GetProperty("title").GetString() ?? "", element.GetProperty("body").GetString() ?? "");
    }
}
