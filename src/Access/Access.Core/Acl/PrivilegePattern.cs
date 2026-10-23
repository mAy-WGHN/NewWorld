using System.Text.RegularExpressions;

namespace Access.Core.Acl;

/// <summary>Privileges <c>resource:action</c>; in roles also <c>*</c> (everything) and <c>area:*</c>.</summary>
public static partial class PrivilegePattern
{
    public const string Everything = "*";

    [GeneratedRegex("^(\\*|[a-z0-9-]+(:[a-z0-9-]+)*:(\\*|[a-z0-9-]+))$")]
    private static partial Regex Format();

    public static bool IsValidPattern(string pattern) => Format().IsMatch(pattern);

    /// <summary>Exact, <c>*</c>, or <c>area:*</c> where the key starts with <c>area:</c>.</summary>
    public static bool Matches(string pattern, string key) =>
        pattern == Everything
        || pattern == key
        || (pattern.EndsWith(":*", StringComparison.Ordinal) && key.StartsWith(pattern[..^1], StringComparison.Ordinal));

    /// <summary>The registered privileges a set of patterns grants; unregistered privileges have no effect.</summary>
    public static IReadOnlyList<string> Expand(IEnumerable<string> patterns, IEnumerable<string> catalogKeys)
    {
        var list = patterns.ToList();
        return catalogKeys.Where(key => list.Any(p => Matches(p, key))).Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>A pattern is known if it is <c>*</c>, a registered key, or <c>area:*</c> matching at least one registered key.</summary>
    public static bool IsKnown(string pattern, IEnumerable<string> catalogKeys) =>
        IsValidPattern(pattern) && (pattern == Everything || catalogKeys.Any(k => Matches(pattern, k)));
}
