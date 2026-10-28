namespace Notifications.Core.Templates;

/// <summary>The name of the person a notification is rendered for (from the copy of the employees). Organisation data (class 1).</summary>
public sealed record RecipientName(string FirstName, string LastName);

/// <summary>A variable a text may use.</summary>
/// <param name="Name">As written after the dollar sign, e.g. <c>USER.firstName</c>.</param>
/// <param name="Sensitive">Class 2: only allowed where the channel and the template allow it.</param>
/// <param name="FromRecipient"><c>true</c> = Notifications fills it in from the recipient; <c>false</c> = the sender delivers it in <c>Data</c>.</param>
public sealed record VariableInfo(string Name, bool Sensitive, bool FromRecipient);

/// <summary>The variables Notifications resolves itself: the recipient's name. Everything else comes from the sender (declared per template).</summary>
public static class TemplateVariables
{
    public const string FirstName = "USER.firstName";
    public const string LastName = "USER.lastName";

    public static readonly IReadOnlyList<VariableInfo> Recipient =
    [
        new(FirstName, false, true),
        new(LastName, false, true),
    ];

    public static bool IsRecipient(string name) => Recipient.Any(v => v.Name == name);

    public static IReadOnlyDictionary<string, string> Values(IReadOnlyDictionary<string, string> data, RecipientName? name)
    {
        var values = new Dictionary<string, string>(data, StringComparer.Ordinal)
        {
            [FirstName] = name?.FirstName ?? string.Empty,
            [LastName] = name?.LastName ?? string.Empty,
        };
        return values;
    }
}
