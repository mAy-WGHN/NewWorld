using System.Text;
using System.Text.RegularExpressions;

namespace Notifications.Core.Templates;

internal static partial class TemplateText
{
    [GeneratedRegex("\\{([A-Za-z][A-Za-z0-9]*)\\}")]
    private static partial Regex Placeholder();

    public static IEnumerable<string> PlaceholdersIn(string text) => Placeholder().Matches(text).Select(m => m.Groups[1].Value);

    public static bool HasBraces(string text) => text.Contains('{', StringComparison.Ordinal) || text.Contains('}', StringComparison.Ordinal);

    /// <summary>Single pass over the template, so inserted values (which may contain braces) stay as they are.</summary>
    public static string Fill(string text, IReadOnlyDictionary<string, string> data)
    {
        var result = new StringBuilder();
        var position = 0;
        foreach (Match match in Placeholder().Matches(text))
        {
            result.Append(text, position, match.Index - position);
            result.Append(data.TryGetValue(match.Groups[1].Value, out var value) ? value : string.Empty);
            position = match.Index + match.Length;
        }

        return result.Append(text, position, text.Length - position).ToString();
    }
}
