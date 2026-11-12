using System.Security.Cryptography;
using System.Text;

namespace Portal.Core.Logbook;

/// <summary>
/// The overall rating of the situation of the store, 1 (very bad) to 5 (very good): every weekly report has it, it is required, it cannot be removed from the
/// template and it cannot be changed except for its title and hint. It has a fixed section id and five fixed option ids, so every version of the template
/// and every evaluation finds it in the same place; the value of an answer is the position of the chosen option.
/// </summary>
public static class LogbookRating
{
    public const string DefaultTitle = "Wie ist die Gesamtsituation in der Filiale?";
    public const string DefaultHint = "1 = sehr schlecht, 5 = sehr gut";

    public static IReadOnlyList<string> Labels { get; } = ["1 – sehr schlecht", "2 – schlecht", "3 – mittel", "4 – gut", "5 – sehr gut"];

    public static Guid SectionId { get; } = Fixed("section");

    /// <summary>The ids of the options for the values 1 to 5, in this order.</summary>
    public static IReadOnlyList<Guid> OptionIds { get; } = [.. Enumerable.Range(1, 5).Select(value => Fixed($"option:{value}"))];

    private static Guid Fixed(string name) => new(SHA256.HashData(Encoding.UTF8.GetBytes("logbook-rating:" + name))[..16]);

    /// <summary>The section as it always is; only title and hint may differ (they are the district leaders' wording).</summary>
    public static LogbookSection Section(string? title = null, string? hint = null) => new(
        SectionId,
        string.IsNullOrWhiteSpace(title) ? DefaultTitle : title,
        string.IsNullOrWhiteSpace(hint) ? DefaultHint : hint,
        LogbookSectionKind.Rating,
        true,
        [.. OptionIds.Select((id, i) => new LogbookOption(id, Labels[i]))]);

    /// <summary>Whether the section is the rating the way it must be: kind, required and the five options.</summary>
    public static bool IsCanonical(LogbookSection section) =>
        section.Id == SectionId && section.Kind == LogbookSectionKind.Rating && section.Required
        && section.Options.Select(o => o.Id).SequenceEqual(OptionIds);

    /// <summary>1 to 5 for the id of an option of the rating, otherwise <c>null</c>.</summary>
    public static int? ValueOf(Guid optionId) => OptionIds.ToList().IndexOf(optionId) is var index and >= 0 ? index + 1 : null;

    /// <summary>The rating in the answers of a report, <c>null</c> if there is none (a report of a version without it, or not answered).</summary>
    public static int? ValueOf(IReadOnlyDictionary<Guid, LogbookAnswer> answers) =>
        answers.TryGetValue(SectionId, out var answer) && answer.OptionIds.Count == 1 ? ValueOf(answer.OptionIds[0]) : null;
}
