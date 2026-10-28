namespace Notifications.Core.Templates;

public enum TemplateProblem
{
    None = 0,
    EmptyText = 1,

    /// <summary>A variable that is neither a recipient variable nor declared by the template.</summary>
    UnknownVariable = 2,

    /// <summary>Class 2 variable in a push text or a mail subject: never allowed.</summary>
    SensitiveInPushOrSubject = 3,

    /// <summary>Class 2 variable in the mail text of a template whose recipients may not receive class 2 by mail.</summary>
    SensitiveInMail = 4,

    /// <summary>A brace that belongs to no variable (push and mail).</summary>
    StrayBraces = 5,

    /// <summary>A declared data variable uses a name reserved for the recipient (<c>USER.…</c>).</summary>
    ReservedName = 6,

    TooLong = 7,
}

/// <summary>
/// The rules for the texts of a template, applied to the files at start and to every text HR saves. The class of a variable and the channel
/// decide where it may stand: push text and mail subject class 1; mail text class 1, class 2 only if the template allows it (recipients such as HR);
/// in-app text class 2. Class 3 cannot occur (it never travels on the bus).
/// </summary>
public static class TemplateRules
{
    public const int MaxTitle = 200;
    public const int MaxBody = 2000;

    public static TemplateProblem Check(Template template)
    {
        if (template.Placeholders.Keys.Any(k => k.StartsWith("USER.", StringComparison.Ordinal)))
        {
            return TemplateProblem.ReservedName;
        }

        foreach (var text in new[] { template.InApp, template.Push, template.Email })
        {
            if (string.IsNullOrWhiteSpace(text.Title) || string.IsNullOrWhiteSpace(text.Body))
            {
                return TemplateProblem.EmptyText;
            }

            if (text.Title.Length > MaxTitle || text.Body.Length > MaxBody)
            {
                return TemplateProblem.TooLong;
            }
        }

        if (new[] { template.Push.Title, template.Push.Body, template.Email.Title, template.Email.Body }.Any(TemplateText.HasStrayBraces))
        {
            return TemplateProblem.StrayBraces;
        }

        foreach (var variable in new[] { template.InApp, template.Push, template.Email }.SelectMany(Template.VariablesIn).Distinct())
        {
            if (!TemplateVariables.IsRecipient(variable) && !template.Placeholders.ContainsKey(variable))
            {
                return TemplateProblem.UnknownVariable;
            }
        }

        if (Sensitive(template, template.Push.Title) || Sensitive(template, template.Push.Body) || Sensitive(template, template.Email.Title))
        {
            return TemplateProblem.SensitiveInPushOrSubject;
        }

        return Sensitive(template, template.Email.Body) && !template.MailMayContainSensitive ? TemplateProblem.SensitiveInMail : TemplateProblem.None;
    }

    private static bool Sensitive(Template template, string text) =>
        TemplateText.PlaceholdersIn(text).Any(v => template.Placeholders.TryGetValue(v, out var sensitive) && sensitive);
}
