using System.Security.Cryptography;
using System.Text;

namespace Access.Core.Publishing;

/// <summary>Last published state of a person (per kind): strictly rising version and a fingerprint of the content.</summary>
public sealed record Publication(long Version, string Fingerprint)
{
    /// <summary>The next publication, or <c>null</c> if nothing changed.</summary>
    public static Publication? Next(Publication? previous, string fingerprint) =>
        previous is not null && previous.Fingerprint == fingerprint ? null : new Publication((previous?.Version ?? 0) + 1, fingerprint);
}

public enum PublicationKind
{
    Unknown = 0,
    Permissions = 1,
    Responsibilities = 2,
}

/// <summary>Order-independent fingerprint of a published state.</summary>
public static class Fingerprint
{
    public static string Of(IReadOnlyDictionary<string, string[]> content)
    {
        var text = new StringBuilder();
        foreach (var (key, values) in content.OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            text.Append(key).Append('=').AppendJoin(',', values.Order(StringComparer.Ordinal)).Append(';');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
}
