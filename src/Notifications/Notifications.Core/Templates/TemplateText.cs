using System.Text;
using System.Text.RegularExpressions;

namespace Notifications.Core.Templates;

/// <summary>
/// The variables of a text: <c>$USER.firstName</c> (dotted names after a dollar sign) and the older <c>{name}</c>. Both name either a
/// recipient variable (<see cref="TemplateVariables"/>) or a data key of the sender declared by the template.
/// </summary>
internal static partial class TemplateText
{
    [GeneratedRegex("\\{([A-Za-z][A-Za-z0-9]*)\\}|\\$([A-Za-z][A-Za-z0-9]*(?:\\.[A-Za-z][A-Za-z0-9]*)*)")]
    private static partial Regex Variable();

    public static IEnumerable<string> PlaceholdersIn(string text) => Variable().Matches(text).Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);

    /// <summary>A brace that is not part of a <c>{name}</c> variable.</summary>
    public static bool HasStrayBraces(string text)
    {
        var rest = Variable().Replace(text, string.Empty);
        return rest.Contains('{', StringComparison.Ordinal) || rest.Contains('}', StringComparison.Ordinal);
    }

    /// <summary>Single pass over the template, so inserted values (which may contain braces or dollar signs) stay as they are.</summary>
    public static string Fill(string text, IReadOnlyDictionary<string, string> values)
    {
        var result = new StringBuilder();
        var position = 0;
        foreach (Match match in Variable().Matches(text))
        {
            result.Append(text, position, match.Index - position);
            var name = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            result.Append(values.TryGetValue(name, out var value) ? value : string.Empty);
            position = match.Index + match.Length;
        }

        return result.Append(text, position, text.Length - position).ToString();
    }
}
