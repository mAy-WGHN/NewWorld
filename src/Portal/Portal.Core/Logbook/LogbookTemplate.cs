using System.Security.Cryptography;
using System.Text;

namespace Portal.Core.Logbook;

/// <summary>The kind of a section of the weekly report: one option, several options, free text, or the overall rating 1 to 5 (exactly one, see <see cref="LogbookRating"/>).</summary>
public enum LogbookSectionKind
{
    Unknown = 0,
    SingleChoice = 1,
    MultiChoice = 2,
    FreeText = 3,
    Rating = 4,
}

/// <summary>Limits of the template and of the answers (so that nobody saves a book instead of a report).</summary>
public static class LogbookLimits
{
    public const int MaxSections = 30;
    public const int MaxOptions = 20;
    public const int MaxTitle = 120;
    public const int MaxHint = 300;
    public const int MaxOptionLabel = 80;
    public const int MaxText = 4000;
}

/// <summary>An option of a choice section. The id stays when the label is changed, so that answers already given keep their meaning.</summary>
public sealed record LogbookOption(Guid Id, string Label);

/// <summary>One section of the report; <see cref="Options"/> only for choice sections.</summary>
public sealed record LogbookSection(Guid Id, string Title, string? Hint, LogbookSectionKind Kind, bool Required, IReadOnlyList<LogbookOption> Options);

/// <summary>
/// A version of the template (decision 49). A change of the content makes a new version; reports keep the version they were filled in with.
/// Version 0 is <see cref="Default"/>: it is never stored, it applies until the first version is saved.
/// </summary>
public sealed record LogbookTemplate(int Version, IReadOnlyList<LogbookSection> Sections, DateTimeOffset? PublishedAt, Guid? PublishedBy)
{
    /// <summary>A suggestion that works from the start; the district leaders change it as they like. The ids are derived from names, so they are always the same.</summary>
    public static LogbookTemplate Default { get; } = BuildDefault();

    /// <summary>Whether the sections are the same as in this version (order, kinds, required, texts without the spaces around, options); then there is no new version.</summary>
    public bool HasSameContent(IReadOnlyList<LogbookSection> other) =>
        Sections.Count == other.Count && Sections.Zip(other).All(pair => SameSection(pair.First, pair.Second));

    private static bool SameSection(LogbookSection a, LogbookSection b) =>
        a.Id == b.Id && Clean(a.Title) == Clean(b.Title) && Clean(a.Hint) == Clean(b.Hint) && a.Kind == b.Kind && a.Required == b.Required
        && a.Options.Count == b.Options.Count && a.Options.Zip(b.Options).All(o => o.First.Id == o.Second.Id && Clean(o.First.Label) == Clean(o.Second.Label));

    private static string Clean(string? text) => (text ?? string.Empty).Trim();

    private static LogbookTemplate BuildDefault()
    {
        static Guid Id(string name) => new(SHA256.HashData(Encoding.UTF8.GetBytes("logbook-default:" + name))[..16]);
        static LogbookSection Choice(string key, string title, LogbookSectionKind kind, bool required, params string[] labels) =>
            new(Id(key), title, null, kind, required, [.. labels.Select((label, i) => new LogbookOption(Id($"{key}:{i}"), label))]);
        static LogbookSection Text(string key, string title, string hint) => new(Id(key), title, hint, LogbookSectionKind.FreeText, false, []);

        return new LogbookTemplate(
            0,
            [
                LogbookRating.Section(),
                Choice("staff", "Wie ist die Personalsituation?", LogbookSectionKind.SingleChoice, true, "Voll besetzt", "Engpässe", "Kritisch"),
                Choice("topics", "Was belastet die Filiale?", LogbookSectionKind.MultiChoice, false, "Personal", "Kasse und Technik", "Ware und Lieferung", "Kundschaft", "Gebäude und Sauberkeit", "Sicherheit", "Sonstiges"),
                Text("good", "Was lief gut?", "Kurz und konkret. Bitte keine Gesundheitsdaten."),
                Text("problems", "Probleme und Sorgen", "Was braucht die Aufmerksamkeit der Regionalleitung? Bitte keine Gesundheitsdaten."),
                Choice("help", "Brauchst du Unterstützung?", LogbookSectionKind.SingleChoice, true, "Nein", "Ja, in den nächsten Tagen", "Ja, dringend"),
            ],
            null,
            null);
    }
}
