using System.Text.RegularExpressions;

namespace Shared.Infrastructure.Audit;

/// <summary>Per service: which record types are not audited (copies from messages, technical or secret tables).</summary>
public sealed partial class AuditOptions
{
    private readonly HashSet<Type> _excluded = [typeof(AuditEntryRecord), typeof(Messaging.OutboxMessage), typeof(Messaging.InboxMessage), typeof(Authorization.PermissionSnapshotRecord)];

    /// <summary>Words in a property name (PascalCase) whose value is never written to the log, whatever the entity.</summary>
    private static readonly HashSet<string> SecretWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "pin", "hash", "secret", "token", "password", "encrypted", "wage", "iban", "salt", "nonce",
    };

    public AuditOptions Exclude<T>() => Exclude(typeof(T));

    public AuditOptions Exclude(Type type)
    {
        _excluded.Add(type);
        return this;
    }

    public bool IsExcluded(Type type) => _excluded.Contains(type);

    public static bool IsSecret(string propertyName) =>
        WordPattern().Matches(propertyName).Any(m => SecretWords.Contains(m.Value));

    [GeneratedRegex("[A-Z]?[a-z]+|[A-Z]+(?![a-z])|[0-9]+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex WordPattern();
}
