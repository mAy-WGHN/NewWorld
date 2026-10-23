namespace Notifications.Core.Templates;

/// <summary>Title and body (or mail subject and body) of one channel.</summary>
public sealed record TextPair(string Title, string Body);

/// <summary>
/// A notification template, identified by <c>&lt;service&gt;.&lt;occasion&gt;.&lt;recipient role&gt;</c>. The in-app text may use declared
/// <c>{placeholders}</c>; push and mail texts are generic and contain no placeholders at all (checked when the catalogue is built).
/// A placeholder marked sensitive carries class 2 content and may only be filled for in-app delivery.
/// </summary>
/// <param name="Key">Template key.</param>
/// <param name="Mandatory">Cannot be switched off by the recipient.</param>
/// <param name="Placeholders">Declared placeholders; <c>true</c> = sensitive (class 2). Undeclared data keys are refused.</param>
/// <param name="InApp">Text stored in the portal.</param>
/// <param name="Push">Generic push text.</param>
/// <param name="Email">Generic mail subject and text.</param>
public sealed record Template(
    string Key,
    bool Mandatory,
    IReadOnlyDictionary<string, bool> Placeholders,
    TextPair InApp,
    TextPair Push,
    TextPair Email)
{
    /// <summary>Placeholders the in-app text needs a value for.</summary>
    public IReadOnlyList<string> RequiredPlaceholders => TemplateText.PlaceholdersIn(InApp.Title).Concat(TemplateText.PlaceholdersIn(InApp.Body)).Distinct().ToList();

    /// <summary>The in-app text with values inserted literally (a value is never expanded again).</summary>
    public TextPair RenderInApp(IReadOnlyDictionary<string, string> data) =>
        new(TemplateText.Fill(InApp.Title, data), TemplateText.Fill(InApp.Body, data));
}
