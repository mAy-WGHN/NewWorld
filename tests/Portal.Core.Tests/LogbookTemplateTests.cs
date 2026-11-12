using Portal.Core.Logbook;

namespace Portal.Core.Tests;

/// <summary>
/// The template of the weekly report (decision 49): sections with single choice, multiple choice or free text, kept by the district leaders. The rules for
/// a template that is saved, the rules for answers against it (a draft may be incomplete, a submission needs the required sections), and when a change
/// makes a new version.
/// </summary>
public sealed class LogbookTemplateTests
{
    private static readonly Guid S1 = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid S2 = Guid.Parse("00000000-0000-0000-0000-0000000000a2");
    private static readonly Guid S3 = Guid.Parse("00000000-0000-0000-0000-0000000000a3");
    private static readonly Guid Good = Guid.Parse("00000000-0000-0000-0000-0000000000b1");
    private static readonly Guid Bad = Guid.Parse("00000000-0000-0000-0000-0000000000b2");
    private static readonly Guid Staff = Guid.Parse("00000000-0000-0000-0000-0000000000c1");
    private static readonly Guid Tech = Guid.Parse("00000000-0000-0000-0000-0000000000c2");
    private static readonly Guid Goods = Guid.Parse("00000000-0000-0000-0000-0000000000c3");

    /// <summary>One required single choice, one multiple choice, one optional free text, and the rating that every template has (required).</summary>
    private static IReadOnlyList<LogbookSection> Sections() =>
    [
        new(S1, "Gesamtlage", null, LogbookSectionKind.SingleChoice, true, [new(Good, "Läuft gut"), new(Bad, "Probleme")]),
        new(S2, "Themen", "Mehrere möglich", LogbookSectionKind.MultiChoice, false, [new(Staff, "Personal"), new(Tech, "Technik"), new(Goods, "Ware")]),
        new(S3, "Anmerkungen", null, LogbookSectionKind.FreeText, false, []),
        LogbookRating.Section(),
    ];

    private static Guid Rate(int value) => LogbookRating.OptionIds[value - 1];

    private static LogbookTemplate Template() => new(3, Sections(), null, null);

    private static IReadOnlyList<LogbookTemplateError> Errors(IReadOnlyList<LogbookSection> sections) => [.. LogbookTemplateValidator.Validate(sections).Select(e => e.Code)];

    private static Dictionary<Guid, LogbookAnswer> Answers(params (Guid Section, LogbookAnswer Answer)[] items) => items.ToDictionary(i => i.Section, i => i.Answer);

    private static LogbookAnswer Choice(params Guid[] options) => new(options, null);

    private static LogbookAnswer Text(string text) => new([], text);

    // ---------------------------------------------------------------- the template itself

    [Fact]
    public void A_correct_template_has_no_errors_and_the_default_is_correct_too()
    {
        Errors(Sections()).ShouldBeEmpty();
        LogbookTemplateValidator.Validate(LogbookTemplate.Default.Sections).ShouldBeEmpty();
        LogbookTemplate.Default.Version.ShouldBe(0); // not stored: the version before the first one that is saved
        LogbookTemplate.Default.Sections.Select(s => s.Kind).Distinct().Count().ShouldBe(4); // all four kinds appear
        LogbookTemplate.Default.Sections.Any(s => s.Required).ShouldBeTrue();
    }

    [Fact]
    public void The_default_is_always_the_same_with_the_same_ids_so_that_reports_of_version_0_stay_readable()
    {
        var first = LogbookTemplate.Default;
        var second = LogbookTemplate.Default;

        second.Sections.Select(s => s.Id).ShouldBe(first.Sections.Select(s => s.Id)); // not new ids at every access
        second.Sections.SelectMany(s => s.Options).Select(o => o.Id).ShouldBe(first.Sections.SelectMany(s => s.Options).Select(o => o.Id));
        var ids = first.Sections.Select(s => s.Id).Concat(first.Sections.SelectMany(s => s.Options).Select(o => o.Id)).ToList();
        ids.Distinct().Count().ShouldBe(ids.Count);
        ids.ShouldNotContain(Guid.Empty);
    }

    [Fact]
    public void A_template_needs_at_least_one_section_and_not_too_many()
    {
        Errors([]).ShouldContain(LogbookTemplateError.NoSections);
        var many = Enumerable.Range(0, LogbookLimits.MaxSections + 1).Select(i => new LogbookSection(Guid.NewGuid(), $"Abschnitt {i}", null, LogbookSectionKind.FreeText, false, [])).ToList();
        Errors(many).ShouldContain(LogbookTemplateError.TooManySections);
    }

    [Fact]
    public void A_section_needs_a_title_that_is_not_too_long_and_a_hint_that_is_not_too_long()
    {
        var sections = new List<LogbookSection>
        {
            new(S1, " ", null, LogbookSectionKind.FreeText, false, []),
            new(S2, new string('x', LogbookLimits.MaxTitle + 1), null, LogbookSectionKind.FreeText, false, []),
            new(S3, "Ok", new string('x', LogbookLimits.MaxHint + 1), LogbookSectionKind.FreeText, false, []),
        };

        var errors = LogbookTemplateValidator.Validate(sections);

        errors.ShouldContain(e => e.Code == LogbookTemplateError.TitleMissing && e.SectionId == S1);
        errors.ShouldContain(e => e.Code == LogbookTemplateError.TitleTooLong && e.SectionId == S2);
        errors.ShouldContain(e => e.Code == LogbookTemplateError.HintTooLong && e.SectionId == S3);
    }

    [Fact]
    public void Section_ids_must_be_set_and_unique_and_the_kind_must_be_known()
    {
        Errors([new(Guid.Empty, "A", null, LogbookSectionKind.FreeText, false, [])]).ShouldContain(LogbookTemplateError.IdMissing);
        Errors([new(S1, "A", null, LogbookSectionKind.FreeText, false, []), new(S1, "B", null, LogbookSectionKind.FreeText, false, [])]).ShouldContain(LogbookTemplateError.DuplicateId);
        Errors([new(S1, "A", null, LogbookSectionKind.Unknown, false, [])]).ShouldContain(LogbookTemplateError.UnknownKind);
    }

    [Fact]
    public void A_choice_section_needs_two_to_twenty_options_with_unique_labels_and_a_free_text_section_has_none()
    {
        var one = new LogbookSection(S1, "A", null, LogbookSectionKind.SingleChoice, false, [new(Good, "Nur eine")]);
        var tooMany = new LogbookSection(S2, "B", null, LogbookSectionKind.MultiChoice, false, [.. Enumerable.Range(0, LogbookLimits.MaxOptions + 1).Select(i => new LogbookOption(Guid.NewGuid(), $"O{i}"))]);
        var sameLabel = new LogbookSection(S3, "C", null, LogbookSectionKind.SingleChoice, false, [new(Good, "Ja"), new(Bad, " ja ")]);
        var textWithOptions = new LogbookSection(Guid.NewGuid(), "D", null, LogbookSectionKind.FreeText, false, [new(Staff, "x"), new(Tech, "y")]);

        var errors = LogbookTemplateValidator.Validate([one, tooMany, sameLabel, textWithOptions]);

        errors.ShouldContain(e => e.Code == LogbookTemplateError.TooFewOptions && e.SectionId == S1);
        errors.ShouldContain(e => e.Code == LogbookTemplateError.TooManyOptions && e.SectionId == S2);
        errors.ShouldContain(e => e.Code == LogbookTemplateError.OptionLabelNotUnique && e.SectionId == S3);
        errors.ShouldContain(e => e.Code == LogbookTemplateError.OptionsNotAllowed && e.SectionId == textWithOptions.Id);
    }

    [Fact]
    public void An_option_needs_an_id_and_a_label_that_is_not_too_long()
    {
        var s = new LogbookSection(S1, "A", null, LogbookSectionKind.SingleChoice, false, [new(Guid.Empty, "Ja"), new(Bad, ""), new(Good, new string('x', LogbookLimits.MaxOptionLabel + 1))]);

        var errors = Errors([s]);

        errors.ShouldContain(LogbookTemplateError.IdMissing);
        errors.ShouldContain(LogbookTemplateError.OptionLabelMissing);
        errors.ShouldContain(LogbookTemplateError.OptionLabelTooLong);
    }

    // ---------------------------------------------------------------- the rating 1 to 5 (always there, required, not removable)

    [Fact]
    public void Every_template_needs_the_rating_and_it_cannot_be_removed()
    {
        var without = Sections().Where(s => s.Id != LogbookRating.SectionId).ToList();

        var errors = LogbookTemplateValidator.Validate(without);

        errors.ShouldContain(e => e.Code == LogbookTemplateError.RatingMissing && e.SectionId == LogbookRating.SectionId);
        Errors([new(S3, "Nur Text", null, LogbookSectionKind.FreeText, false, [])]).ShouldContain(LogbookTemplateError.RatingMissing);
        Errors(Sections()).ShouldNotContain(LogbookTemplateError.RatingMissing);
    }

    [Fact]
    public void The_rating_cannot_be_changed_except_for_its_wording()
    {
        LogbookTemplateError[] Changed(Func<LogbookSection, LogbookSection> change) =>
            [.. LogbookTemplateValidator.Validate([.. Sections().Select(s => s.Id == LogbookRating.SectionId ? change(s) : s)]).Select(e => e.Code)];

        Changed(s => s with { Required = false }).ShouldContain(LogbookTemplateError.RatingChanged); // not optional
        Changed(s => s with { Kind = LogbookSectionKind.SingleChoice }).ShouldContain(LogbookTemplateError.RatingChanged);
        Changed(s => s with { Kind = LogbookSectionKind.FreeText, Options = [] }).ShouldContain(LogbookTemplateError.RatingChanged);
        Changed(s => s with { Options = [.. s.Options.Take(4)] }).ShouldContain(LogbookTemplateError.RatingChanged); // five values, not four
        Changed(s => s with { Options = [.. s.Options.Reverse()] }).ShouldContain(LogbookTemplateError.RatingChanged); // the position is the value
        Changed(s => s with { Options = [.. s.Options, new LogbookOption(Guid.NewGuid(), "6")] }).ShouldContain(LogbookTemplateError.RatingChanged);
        Changed(s => s with { Title = "Gesamtlage der Filiale", Hint = "Ehrlich bewerten" }).ShouldBeEmpty(); // the wording is the district leaders'
    }

    [Fact]
    public void There_is_only_one_rating_and_no_other_section_can_be_one()
    {
        var other = new LogbookSection(Guid.NewGuid(), "Noch eine Bewertung", null, LogbookSectionKind.Rating, true, [.. LogbookRating.OptionIds.Select(id => new LogbookOption(id, "x"))]);

        Errors([.. Sections(), other]).ShouldContain(LogbookTemplateError.RatingNotAllowed);
        Errors([.. Sections(), LogbookRating.Section()]).ShouldContain(LogbookTemplateError.DuplicateId);
    }

    [Fact]
    public void The_rating_has_fixed_ids_and_values_from_1_to_5_and_the_default_template_has_it_first()
    {
        LogbookRating.OptionIds.Count.ShouldBe(5);
        LogbookRating.OptionIds.Distinct().Count().ShouldBe(5);
        LogbookRating.OptionIds.ShouldNotContain(LogbookRating.SectionId);
        LogbookRating.Labels.Count.ShouldBe(5);
        LogbookRating.Section().Options.Select(o => o.Label).ShouldBe(LogbookRating.Labels);
        for (var value = 1; value <= 5; value++)
        {
            LogbookRating.ValueOf(LogbookRating.OptionIds[value - 1]).ShouldBe(value);
        }

        LogbookRating.ValueOf(Guid.NewGuid()).ShouldBeNull();
        LogbookTemplate.Default.Sections[0].Id.ShouldBe(LogbookRating.SectionId);
        LogbookRating.IsCanonical(LogbookTemplate.Default.Sections[0]).ShouldBeTrue();
        LogbookTemplate.Default.Sections.Count(s => s.Kind == LogbookSectionKind.Rating).ShouldBe(1);
    }

    [Fact]
    public void The_id_of_the_rating_is_fixed_because_the_frontend_and_old_reports_rely_on_it()
    {
        // The same value is RATING_SECTION_ID in the frontend (logbook-models.ts); change both together or never.
        LogbookRating.SectionId.ShouldBe(Guid.Parse("98d00f7f-28bc-a7c4-27d6-05c7283fc341"));
    }

    [Fact]
    public void The_rating_is_read_from_the_answers_of_a_report()
    {
        LogbookRating.ValueOf(Answers((LogbookRating.SectionId, Choice(Rate(2))))).ShouldBe(2);
        LogbookRating.ValueOf(Answers()).ShouldBeNull(); // a report of a version without it, or not answered
        LogbookRating.ValueOf(Answers((LogbookRating.SectionId, Choice()))).ShouldBeNull();
        LogbookRating.ValueOf(Answers((LogbookRating.SectionId, Choice(Rate(1), Rate(2))))).ShouldBeNull(); // one value, never two
        LogbookRating.ValueOf(Answers((S1, Choice(Good)))).ShouldBeNull();
    }

    [Fact]
    public void An_answer_for_the_rating_is_one_of_the_five_values_and_the_submission_needs_it()
    {
        var template = Template();

        LogbookAnswerValidator.Validate(template, Answers((LogbookRating.SectionId, Choice(Rate(5)))), forSubmit: false).ShouldBeEmpty();
        LogbookAnswerValidator.Validate(template, Answers((LogbookRating.SectionId, Choice(Rate(1), Rate(5)))), forSubmit: false).Select(e => e.Code).ShouldContain(LogbookAnswerError.TooManySelections);
        LogbookAnswerValidator.Validate(template, Answers((LogbookRating.SectionId, Choice(Good))), forSubmit: false).Select(e => e.Code).ShouldContain(LogbookAnswerError.UnknownOption);
        LogbookAnswerValidator.Validate(template, Answers((LogbookRating.SectionId, new LogbookAnswer([], "Gut"))), forSubmit: false).Select(e => e.Code).ShouldContain(LogbookAnswerError.TextNotAllowed);
        LogbookAnswerValidator.Validate(template, Answers((S1, Choice(Good))), forSubmit: true).ShouldContain(e => e.Code == LogbookAnswerError.RequiredMissing && e.SectionId == LogbookRating.SectionId);
    }

    // ---------------------------------------------------------------- versions


    [Fact]
    public void The_same_content_is_not_a_new_version_but_every_change_that_matters_is()
    {
        var template = Template();

        template.HasSameContent(Sections()).ShouldBeTrue();
        template.HasSameContent([.. Sections().Reverse()]).ShouldBeFalse(); // the order is part of the content
        template.HasSameContent([.. Sections().Select(s => s with { Title = s.Title + "!" })]).ShouldBeFalse();
        template.HasSameContent([.. Sections().Select(s => s with { Required = !s.Required })]).ShouldBeFalse();
        template.HasSameContent([.. Sections().Select(s => s.Kind == LogbookSectionKind.SingleChoice ? s with { Options = [.. s.Options, new LogbookOption(Guid.NewGuid(), "Neu")] } : s)]).ShouldBeFalse();
        template.HasSameContent([.. Sections().Select(s => s with { Title = "  " + s.Title + "  " })]).ShouldBeTrue(); // spaces around do not count
    }

    // ---------------------------------------------------------------- answers

    [Fact]
    public void A_complete_answer_is_valid_for_a_draft_and_for_the_submission()
    {
        var answers = Answers((S1, Choice(Good)), (S2, Choice(Staff, Tech)), (S3, Text("Alles ruhig.")), (LogbookRating.SectionId, Choice(Rate(4))));

        LogbookAnswerValidator.Validate(Template(), answers, forSubmit: false).ShouldBeEmpty();
        LogbookAnswerValidator.Validate(Template(), answers, forSubmit: true).ShouldBeEmpty();
    }

    [Fact]
    public void A_draft_may_be_incomplete_but_the_submission_needs_every_required_section()
    {
        var nothing = Answers();

        LogbookAnswerValidator.Validate(Template(), nothing, forSubmit: false).ShouldBeEmpty();
        var missing = LogbookAnswerValidator.Validate(Template(), nothing, forSubmit: true);
        missing.Select(e => (e.Code, e.SectionId)).ShouldBe([(LogbookAnswerError.RequiredMissing, S1), (LogbookAnswerError.RequiredMissing, LogbookRating.SectionId)]); // S1 and the rating are required
        LogbookAnswerValidator.Validate(Template(), Answers((S1, Choice())), forSubmit: true).Select(e => e.Code).ShouldContain(LogbookAnswerError.RequiredMissing);
        LogbookAnswerValidator.Validate(Template(), Answers((S1, new LogbookAnswer([], "text"))), forSubmit: true).Select(e => e.Code).ShouldContain(LogbookAnswerError.RequiredMissing);
    }

    [Fact]
    public void A_required_free_text_needs_more_than_spaces()
    {
        var template = new LogbookTemplate(1, [new(S3, "Text", null, LogbookSectionKind.FreeText, true, [])], null, null);

        LogbookAnswerValidator.Validate(template, Answers((S3, Text("   "))), forSubmit: true).Select(e => e.Code).ShouldContain(LogbookAnswerError.RequiredMissing);
        LogbookAnswerValidator.Validate(template, Answers((S3, Text("Etwas"))), forSubmit: true).ShouldBeEmpty();
    }

    [Fact]
    public void An_answer_must_belong_to_a_section_of_the_template_and_to_an_option_of_that_section()
    {
        var unknownSection = Answers((Guid.NewGuid(), Choice(Good)));
        var foreignOption = Answers((S1, Choice(Staff))); // Staff belongs to S2
        var unknownOption = Answers((S1, Choice(Guid.NewGuid())));

        LogbookAnswerValidator.Validate(Template(), unknownSection, forSubmit: false).Select(e => e.Code).ShouldContain(LogbookAnswerError.UnknownSection);
        LogbookAnswerValidator.Validate(Template(), foreignOption, forSubmit: false).Select(e => e.Code).ShouldContain(LogbookAnswerError.UnknownOption);
        LogbookAnswerValidator.Validate(Template(), unknownOption, forSubmit: false).Select(e => e.Code).ShouldContain(LogbookAnswerError.UnknownOption);
    }

    [Fact]
    public void A_single_choice_takes_one_option_a_multiple_choice_each_option_once()
    {
        LogbookAnswerValidator.Validate(Template(), Answers((S1, Choice(Good, Bad))), forSubmit: false).Select(e => e.Code).ShouldContain(LogbookAnswerError.TooManySelections);
        LogbookAnswerValidator.Validate(Template(), Answers((S2, Choice(Staff, Staff))), forSubmit: false).Select(e => e.Code).ShouldContain(LogbookAnswerError.OptionChosenTwice);
        LogbookAnswerValidator.Validate(Template(), Answers((S2, Choice(Staff, Tech, Goods))), forSubmit: false).ShouldBeEmpty();
    }

    [Fact]
    public void Text_belongs_only_to_free_text_and_options_only_to_choices_and_text_has_a_limit()
    {
        LogbookAnswerValidator.Validate(Template(), Answers((S1, new LogbookAnswer([Good], "Anmerkung"))), forSubmit: false).Select(e => e.Code).ShouldContain(LogbookAnswerError.TextNotAllowed);
        LogbookAnswerValidator.Validate(Template(), Answers((S3, new LogbookAnswer([Good], null))), forSubmit: false).Select(e => e.Code).ShouldContain(LogbookAnswerError.OptionsNotAllowed);
        LogbookAnswerValidator.Validate(Template(), Answers((S3, Text(new string('x', LogbookLimits.MaxText + 1)))), forSubmit: false).Select(e => e.Code).ShouldContain(LogbookAnswerError.TextTooLong);
        LogbookAnswerValidator.Validate(Template(), Answers((S3, Text(new string('x', LogbookLimits.MaxText)))), forSubmit: false).ShouldBeEmpty();
    }

    [Fact]
    public void The_errors_of_an_answer_name_the_section()
    {
        var errors = LogbookAnswerValidator.Validate(Template(), Answers((S1, Choice(Good, Bad)), (S3, Text(new string('x', LogbookLimits.MaxText + 1)))), forSubmit: false);

        errors.ShouldContain(e => e.Code == LogbookAnswerError.TooManySelections && e.SectionId == S1);
        errors.ShouldContain(e => e.Code == LogbookAnswerError.TextTooLong && e.SectionId == S3);
    }
}
