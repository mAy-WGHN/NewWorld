namespace Portal.Core.Logbook;

/// <summary>The answer to one section: the chosen options (choice sections) or the text (free text).</summary>
public sealed record LogbookAnswer(IReadOnlyList<Guid> OptionIds, string? Text);

/// <summary>What is wrong with an answer.</summary>
public enum LogbookAnswerError
{
    Unknown = 0,
    UnknownSection = 1,
    UnknownOption = 2,
    TooManySelections = 3,
    OptionChosenTwice = 4,
    TextNotAllowed = 5,
    OptionsNotAllowed = 6,
    TextTooLong = 7,
    RequiredMissing = 8,
}

/// <summary>One error with the section it is about.</summary>
public sealed record LogbookAnswerProblem(LogbookAnswerError Code, Guid SectionId);

/// <summary>
/// The rules for the answers of a report against the template version it is filled in with. A draft may be incomplete; the submission needs every required
/// section answered (an option chosen, text that is more than spaces). Answers must belong to sections of the template and options of their section.
/// </summary>
public static class LogbookAnswerValidator
{
    public static IReadOnlyList<LogbookAnswerProblem> Validate(LogbookTemplate template, IReadOnlyDictionary<Guid, LogbookAnswer> answers, bool forSubmit)
    {
        var errors = new List<LogbookAnswerProblem>();
        var sections = template.Sections.ToDictionary(s => s.Id);

        foreach (var (sectionId, answer) in answers)
        {
            if (!sections.TryGetValue(sectionId, out var section))
            {
                errors.Add(new(LogbookAnswerError.UnknownSection, sectionId));
                continue;
            }

            ValidateAnswer(section, answer, errors);
        }

        if (forSubmit)
        {
            foreach (var section in template.Sections.Where(s => s.Required))
            {
                if (!answers.TryGetValue(section.Id, out var answer) || !IsAnswered(section, answer))
                {
                    errors.Add(new(LogbookAnswerError.RequiredMissing, section.Id));
                }
            }
        }

        return errors;
    }

    private static void ValidateAnswer(LogbookSection section, LogbookAnswer answer, List<LogbookAnswerProblem> errors)
    {
        var id = section.Id;
        if (section.Kind == LogbookSectionKind.FreeText)
        {
            if (answer.OptionIds.Count > 0)
            {
                errors.Add(new(LogbookAnswerError.OptionsNotAllowed, id));
            }

            if (answer.Text is { } text && text.Length > LogbookLimits.MaxText)
            {
                errors.Add(new(LogbookAnswerError.TextTooLong, id));
            }

            return;
        }

        if (!string.IsNullOrWhiteSpace(answer.Text))
        {
            errors.Add(new(LogbookAnswerError.TextNotAllowed, id));
        }

        var known = section.Options.Select(o => o.Id).ToHashSet();
        if (answer.OptionIds.Any(o => !known.Contains(o)))
        {
            errors.Add(new(LogbookAnswerError.UnknownOption, id));
        }

        if (section.Kind is LogbookSectionKind.SingleChoice or LogbookSectionKind.Rating && answer.OptionIds.Count > 1)
        {
            errors.Add(new(LogbookAnswerError.TooManySelections, id));
        }
        else if (answer.OptionIds.Count != answer.OptionIds.Distinct().Count())
        {
            errors.Add(new(LogbookAnswerError.OptionChosenTwice, id));
        }
    }

    private static bool IsAnswered(LogbookSection section, LogbookAnswer answer) =>
        section.Kind == LogbookSectionKind.FreeText
            ? !string.IsNullOrWhiteSpace(answer.Text)
            : answer.OptionIds.Any(o => section.Options.Any(x => x.Id == o));
}
