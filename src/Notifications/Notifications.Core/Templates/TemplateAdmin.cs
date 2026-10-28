using System.Globalization;
using Notifications.Contracts;

namespace Notifications.Core.Templates;

/// <summary>The texts HR can change. <c>null</c> = the text of the file (default).</summary>
public sealed record TemplateTexts(TextPair? InApp, TextPair? Push, TextPair? Email);

/// <summary>A saved change of the texts of one template.</summary>
public sealed record TemplateOverride(string Key, TemplateTexts Texts, DateTimeOffset ChangedAt, Guid ChangedBy);

/// <summary>The changed texts HR maintains (the files stay the defaults).</summary>
public interface ITemplateOverrides
{
    Task<TemplateOverride?> GetAsync(string key, CancellationToken cancellationToken);

    Task<IReadOnlyList<TemplateOverride>> ListAsync(CancellationToken cancellationToken);

    Task SaveAsync(TemplateOverride change, CancellationToken cancellationToken);

    /// <returns><c>false</c> if the template had no changed texts.</returns>
    Task<bool> DeleteAsync(string key, CancellationToken cancellationToken);
}

/// <summary>The names of recipients (copy of the employees), for <c>$USER.firstName</c> and <c>$USER.lastName</c>.</summary>
public interface IRecipientDirectory
{
    Task<IReadOnlyDictionary<Guid, RecipientName>> NamesAsync(IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken);
}

/// <summary>A template as the maintenance sees it: default texts, effective texts and what may be used where.</summary>
public sealed record TemplateView(
    string Key, bool Mandatory, bool SensitiveText, bool MailMayContainSensitive, bool Customized,
    TemplateTexts Default, TemplateTexts Effective, IReadOnlyList<VariableInfo> Variables, DateTimeOffset? ChangedAt, Guid? ChangedBy);

public enum TemplateAdminError
{
    None = 0,
    UnknownTemplate = 1,

    /// <summary>The texts break a rule (see <see cref="TemplateSaveResult.Problem"/>).</summary>
    Invalid = 2,

    /// <summary>Push and mail texts of this template are fixed (the text itself is class 2).</summary>
    SensitiveTextLocked = 3,
}

public sealed record TemplateSaveResult(TemplateAdminError Error, TemplateProblem Problem = TemplateProblem.None);

/// <summary>Layers the changed texts of HR over the file defaults.</summary>
public static class TemplateOverrideRules
{
    /// <summary>The template with the changed texts; a change that breaks a rule (e.g. a template changed in a new version) is ignored and the defaults apply.</summary>
    public static Template Apply(Template template, TemplateTexts? texts)
    {
        if (texts is null)
        {
            return template;
        }

        var candidate = template with
        {
            InApp = texts.InApp ?? template.InApp,
            Push = template.SensitiveText ? template.Push : texts.Push ?? template.Push,
            Email = template.SensitiveText ? (template.MailMayContainSensitive && texts.Email is { } mail ? new TextPair(template.Email.Title, mail.Body) : template.Email) : texts.Email ?? template.Email,
        };
        return TemplateRules.Check(candidate) == TemplateProblem.None ? candidate : template;
    }

    /// <summary>
    /// Whether the texts touch what is fixed. In a template whose text itself is class 2 the push text and the mail subject are fixed; the mail text only
    /// if the recipients may not receive class 2 by mail (then everything of push and mail is fixed).
    /// </summary>
    public static bool Locked(Template template, TemplateTexts texts) =>
        template.SensitiveText && (Differs(texts.Push, template.Push) || (template.MailMayContainSensitive
            ? texts.Email is { } mail && mail.Title != template.Email.Title
            : Differs(texts.Email, template.Email)));

    private static bool Differs(TextPair? text, TextPair current) => text is not null && text != current;
}

/// <summary>
/// Maintenance of the texts by HR: list, change (checked against <see cref="TemplateRules"/>), reset to the file default, preview. Callers check the
/// privilege and save the unit of work. Nothing about the texts goes into logs.
/// </summary>
public sealed class TemplateAdminService(TemplateCatalog catalog, ITemplateOverrides overrides, TimeProvider time)
{
    private static readonly IReadOnlyDictionary<string, string> SampleRecipient = new Dictionary<string, string>();

    public async Task<IReadOnlyList<TemplateView>> ListAsync(CancellationToken cancellationToken)
    {
        var changed = (await overrides.ListAsync(cancellationToken)).ToDictionary(o => o.Key, StringComparer.Ordinal);
        return [.. catalog.All.OrderBy(t => t.Key, StringComparer.Ordinal).Select(t => View(t, changed.GetValueOrDefault(t.Key)))];
    }

    public async Task<TemplateView?> GetAsync(string key, CancellationToken cancellationToken) =>
        catalog.TryGet(key, out var template) ? View(template!, await overrides.GetAsync(key, cancellationToken)) : null;

    public async Task<TemplateSaveResult> SaveAsync(string key, TemplateTexts texts, Guid actorId, CancellationToken cancellationToken)
    {
        if (!catalog.TryGet(key, out var found))
        {
            return new TemplateSaveResult(TemplateAdminError.UnknownTemplate);
        }

        var template = found!;
        if (TemplateOverrideRules.Locked(template, texts))
        {
            return new TemplateSaveResult(TemplateAdminError.SensitiveTextLocked);
        }

        // Only what differs from the file is stored: the rest follows the file when it changes.
        var stored = new TemplateTexts(Changes(texts.InApp, template.InApp) ? texts.InApp : null, Changes(texts.Push, template.Push) ? texts.Push : null, Changes(texts.Email, template.Email) ? texts.Email : null);
        var candidate = template with { InApp = stored.InApp ?? template.InApp, Push = stored.Push ?? template.Push, Email = stored.Email ?? template.Email };
        if (TemplateRules.Check(candidate) is var problem and not TemplateProblem.None)
        {
            return new TemplateSaveResult(TemplateAdminError.Invalid, problem);
        }

        if (stored is { InApp: null, Push: null, Email: null })
        {
            await overrides.DeleteAsync(key, cancellationToken);
        }
        else
        {
            await overrides.SaveAsync(new TemplateOverride(key, stored, time.GetUtcNow(), actorId), cancellationToken);
        }

        return new TemplateSaveResult(TemplateAdminError.None);
    }

    /// <summary>Back to the text of the file.</summary>
    public async Task<TemplateAdminError> ResetAsync(string key, CancellationToken cancellationToken)
    {
        if (!catalog.TryGet(key, out _))
        {
            return TemplateAdminError.UnknownTemplate;
        }

        await overrides.DeleteAsync(key, cancellationToken);
        return TemplateAdminError.None;
    }

    /// <summary>How the texts would look, with example values (<c>Hans Dampf</c>; data variables as given or shown in brackets). Nothing is stored.</summary>
    public TemplatePreviewResult Preview(string key, TemplateTexts texts, IReadOnlyDictionary<string, string>? sampleData)
    {
        if (!catalog.TryGet(key, out var found))
        {
            return new TemplatePreviewResult(new TemplateSaveResult(TemplateAdminError.UnknownTemplate), null);
        }

        var template = found!;
        if (TemplateOverrideRules.Locked(template, texts))
        {
            return new TemplatePreviewResult(new TemplateSaveResult(TemplateAdminError.SensitiveTextLocked), null);
        }

        var candidate = template with { InApp = texts.InApp ?? template.InApp, Push = texts.Push ?? template.Push, Email = texts.Email ?? template.Email };
        if (TemplateRules.Check(candidate) is var problem and not TemplateProblem.None)
        {
            return new TemplatePreviewResult(new TemplateSaveResult(TemplateAdminError.Invalid, problem), null);
        }

        var data = candidate.Placeholders.Keys.ToDictionary(k => k, k => sampleData is not null && sampleData.TryGetValue(k, out var v) ? v : $"[{k}]", StringComparer.Ordinal);
        var name = new RecipientName("Hans", "Dampf");
        return new TemplatePreviewResult(
            new TemplateSaveResult(TemplateAdminError.None),
            new TemplateTexts(
                candidate.Render(NotificationChannel.InApp, data, name),
                candidate.Render(NotificationChannel.Push, data, name),
                candidate.Render(NotificationChannel.Email, data, name)));
    }

    private static bool Changes(TextPair? text, TextPair current) => text is not null && text != current;

    private static TemplateView View(Template template, TemplateOverride? change)
    {
        var effective = TemplateOverrideRules.Apply(template, change?.Texts);
        var variables = TemplateVariables.Recipient.Concat(template.Placeholders.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new VariableInfo(p.Key, p.Value, false))).ToList();
        return new TemplateView(
            template.Key, template.Mandatory, template.SensitiveText, template.MailMayContainSensitive, change is not null,
            new TemplateTexts(template.InApp, template.Push, template.Email), new TemplateTexts(effective.InApp, effective.Push, effective.Email),
            variables, change?.ChangedAt, change?.ChangedBy);
    }
}

public sealed record TemplatePreviewResult(TemplateSaveResult Result, TemplateTexts? Rendered);
