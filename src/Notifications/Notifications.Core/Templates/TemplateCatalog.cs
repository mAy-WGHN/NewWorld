using System.Text.RegularExpressions;

namespace Notifications.Core.Templates;

public sealed class TemplateException(string message) : Exception(message);

/// <summary>All templates. Built once at start; a faulty template stops the start instead of failing at the first notification.</summary>
public sealed partial class TemplateCatalog
{
    private readonly Dictionary<string, Template> _byKey = new(StringComparer.Ordinal);

    public TemplateCatalog(IEnumerable<Template> templates)
    {
        foreach (var template in templates)
        {
            Validate(template);
            if (!_byKey.TryAdd(template.Key, template))
            {
                throw new TemplateException($"Template '{template.Key}' exists twice.");
            }
        }
    }

    public IReadOnlyCollection<string> Keys => _byKey.Keys;

    public IReadOnlyCollection<Template> All => _byKey.Values;

    public bool TryGet(string key, out Template? template) => _byKey.TryGetValue(key, out template);

    [GeneratedRegex("^[a-z0-9-]+\\.[a-z0-9-]+\\.[a-z0-9-]+$")]
    private static partial Regex KeyFormat();

    private static void Validate(Template template)
    {
        if (!KeyFormat().IsMatch(template.Key))
        {
            throw new TemplateException($"Template key '{template.Key}' must look like <service>.<occasion>.<recipient role>.");
        }

        if (TemplateRules.Check(template) is var problem and not TemplateProblem.None)
        {
            throw new TemplateException($"Template '{template.Key}': {problem}.");
        }
    }
}
