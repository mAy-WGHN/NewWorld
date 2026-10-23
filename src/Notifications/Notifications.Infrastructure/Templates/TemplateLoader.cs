using Notifications.Core.Templates;

namespace Notifications.Infrastructure.Templates;

/// <summary>Loads the template files (JSON) from a folder. A faulty file stops the start.</summary>
public static class TemplateLoader
{
    public static TemplateCatalog Load(string folder)
    {
        if (!Directory.Exists(folder))
        {
            throw new TemplateException($"Template folder '{folder}' does not exist.");
        }

        return new TemplateCatalog(Directory.EnumerateFiles(folder, "*.json").Order(StringComparer.Ordinal).Select(file =>
        {
            var template = TemplateParser.Parse(File.ReadAllText(file));
            return Path.GetFileNameWithoutExtension(file) == template.Key
                ? template
                : throw new TemplateException($"File '{Path.GetFileName(file)}' must be named after its template key '{template.Key}'.");
        }));
    }
}
