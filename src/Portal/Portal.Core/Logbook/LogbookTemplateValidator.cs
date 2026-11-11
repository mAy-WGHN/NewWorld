namespace Portal.Core.Logbook;

/// <summary>What is wrong with a template that is being saved.</summary>
public enum LogbookTemplateError
{
    Unknown = 0,
    NoSections = 1,
    TooManySections = 2,
    IdMissing = 3,
    DuplicateId = 4,
    TitleMissing = 5,
    TitleTooLong = 6,
    HintTooLong = 7,
    UnknownKind = 8,
    TooFewOptions = 9,
    TooManyOptions = 10,
    OptionsNotAllowed = 11,
    OptionLabelMissing = 12,
    OptionLabelTooLong = 13,
    OptionLabelNotUnique = 14,
}

/// <summary>One error with the id of the section it is about (<see cref="Guid.Empty"/> for the template as a whole).</summary>
public sealed record LogbookTemplateProblem(LogbookTemplateError Code, Guid SectionId);

/// <summary>The rules for the sections of a template that is saved: at least one and at most 30 sections, ids set and unique, titles, kinds, and for choices two to twenty options with different labels.</summary>
public static class LogbookTemplateValidator
{
    public static IReadOnlyList<LogbookTemplateProblem> Validate(IReadOnlyList<LogbookSection> sections)
    {
        var errors = new List<LogbookTemplateProblem>();
        if (sections.Count == 0)
        {
            errors.Add(new(LogbookTemplateError.NoSections, Guid.Empty));
        }

        if (sections.Count > LogbookLimits.MaxSections)
        {
            errors.Add(new(LogbookTemplateError.TooManySections, Guid.Empty));
        }

        foreach (var duplicate in sections.Where(s => s.Id != Guid.Empty).GroupBy(s => s.Id).Where(g => g.Count() > 1))
        {
            errors.Add(new(LogbookTemplateError.DuplicateId, duplicate.Key));
        }

        foreach (var section in sections)
        {
            ValidateSection(section, errors);
        }

        return errors;
    }

    private static void ValidateSection(LogbookSection section, List<LogbookTemplateProblem> errors)
    {
        var id = section.Id;
        if (id == Guid.Empty)
        {
            errors.Add(new(LogbookTemplateError.IdMissing, id));
        }

        if (string.IsNullOrWhiteSpace(section.Title))
        {
            errors.Add(new(LogbookTemplateError.TitleMissing, id));
        }
        else if (section.Title.Trim().Length > LogbookLimits.MaxTitle)
        {
            errors.Add(new(LogbookTemplateError.TitleTooLong, id));
        }

        if (section.Hint is { } hint && hint.Trim().Length > LogbookLimits.MaxHint)
        {
            errors.Add(new(LogbookTemplateError.HintTooLong, id));
        }

        switch (section.Kind)
        {
            case LogbookSectionKind.SingleChoice or LogbookSectionKind.MultiChoice:
                ValidateOptions(section, errors);
                break;
            case LogbookSectionKind.FreeText:
                if (section.Options.Count > 0)
                {
                    errors.Add(new(LogbookTemplateError.OptionsNotAllowed, id));
                }

                break;
            default:
                errors.Add(new(LogbookTemplateError.UnknownKind, id));
                break;
        }
    }

    private static void ValidateOptions(LogbookSection section, List<LogbookTemplateProblem> errors)
    {
        var id = section.Id;
        if (section.Options.Count < 2)
        {
            errors.Add(new(LogbookTemplateError.TooFewOptions, id));
        }

        if (section.Options.Count > LogbookLimits.MaxOptions)
        {
            errors.Add(new(LogbookTemplateError.TooManyOptions, id));
        }

        foreach (var option in section.Options)
        {
            if (option.Id == Guid.Empty)
            {
                errors.Add(new(LogbookTemplateError.IdMissing, id));
            }

            if (string.IsNullOrWhiteSpace(option.Label))
            {
                errors.Add(new(LogbookTemplateError.OptionLabelMissing, id));
            }
            else if (option.Label.Trim().Length > LogbookLimits.MaxOptionLabel)
            {
                errors.Add(new(LogbookTemplateError.OptionLabelTooLong, id));
            }
        }

        if (section.Options.Where(o => o.Id != Guid.Empty).GroupBy(o => o.Id).Any(g => g.Count() > 1))
        {
            errors.Add(new(LogbookTemplateError.DuplicateId, id));
        }

        if (section.Options.Where(o => !string.IsNullOrWhiteSpace(o.Label)).GroupBy(o => o.Label.Trim().ToUpperInvariant()).Any(g => g.Count() > 1))
        {
            errors.Add(new(LogbookTemplateError.OptionLabelNotUnique, id));
        }
    }
}
