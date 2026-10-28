using Notifications.Contracts;

namespace Notifications.Core.Templates;

/// <summary>Title and body (or mail subject and body) of one channel.</summary>
public sealed record TextPair(string Title, string Body);

/// <summary>
/// A notification template, identified by <c>&lt;service&gt;.&lt;occasion&gt;.&lt;recipient role&gt;</c>. The texts may use variables
/// (<c>$USER.firstName</c>, or data the sender declared). Class by channel (<see cref="TemplateRules"/>): push text and mail subject only class 1,
/// mail text class 2 only if <see cref="MailMayContainSensitive"/>, in-app text class 2. The files are the defaults; HR can overwrite the texts
/// (see <see cref="TemplateAdminService"/>), the flags and the declared variables stay in the file (code).
/// </summary>
/// <param name="Key">Template key.</param>
/// <param name="Mandatory">Cannot be switched off by the recipient.</param>
/// <param name="Placeholders">Declared data variables of the sender; <c>true</c> = sensitive (class 2). Undeclared data keys are refused.</param>
/// <param name="InApp">Text stored in the portal.</param>
/// <param name="Push">Push text (class 1 variables only).</param>
/// <param name="Email">Mail subject (class 1 variables only) and text.</param>
/// <param name="SensitiveText">The text itself carries class 2 (e.g. sickness): push text and mail subject stay as in the file; the mail text too unless <paramref name="MailMayContainSensitive"/>.</param>
/// <param name="MailMayContainSensitive">The recipients may receive class 2 in the mail text (e.g. HR); push and subject never.</param>
public sealed record Template(
    string Key,
    bool Mandatory,
    IReadOnlyDictionary<string, bool> Placeholders,
    TextPair InApp,
    TextPair Push,
    TextPair Email,
    bool SensitiveText = false,
    bool MailMayContainSensitive = false)
{
    /// <summary>Data variables the in-app text needs a value for (the recipient's name is not data).</summary>
    public IReadOnlyList<string> RequiredPlaceholders => RequiredFor(NotificationChannel.InApp);

    public TextPair TextFor(NotificationChannel channel) => channel switch
    {
        NotificationChannel.Push => Push,
        NotificationChannel.Email => Email,
        _ => InApp,
    };

    /// <summary>Data variables the text of the channel needs a value for.</summary>
    public IReadOnlyList<string> RequiredFor(NotificationChannel channel) => VariablesIn(TextFor(channel)).Where(v => !TemplateVariables.IsRecipient(v)).ToList();

    /// <summary>All variables a text uses, the recipient's among them.</summary>
    public static IReadOnlyList<string> VariablesIn(TextPair text) => TemplateText.PlaceholdersIn(text.Title).Concat(TemplateText.PlaceholdersIn(text.Body)).Distinct().ToList();

    /// <summary>The in-app text with values inserted literally (a value is never expanded again).</summary>
    public TextPair RenderInApp(IReadOnlyDictionary<string, string> data, RecipientName? recipient = null) => Render(NotificationChannel.InApp, data, recipient);

    public TextPair Render(NotificationChannel channel, IReadOnlyDictionary<string, string> data, RecipientName? recipient)
    {
        var text = TextFor(channel);
        var values = TemplateVariables.Values(data, recipient);
        return new(TemplateText.Fill(text.Title, values), TemplateText.Fill(text.Body, values));
    }
}
