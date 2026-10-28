using Notifications.Contracts;
using Notifications.Core.Delivery;
using Notifications.Core.Templates;
using Microsoft.Extensions.Time.Testing;
using static Notifications.Contracts.NotificationChannel;
using static Notifications.Core.Tests.Samples;

namespace Notifications.Core.Tests;

/// <summary>Variables in the texts ($USER.firstName, data of the sender) and the class rules per channel.</summary>
public class TemplateVariablesTests
{
    private static Template With(Func<Template, Template> change) => change(News);

    private static TemplateProblem Problem(Func<Template, Template> change) => TemplateRules.Check(With(change));

    // ---------------------------------------------------------------- rules

    [Fact]
    public void The_recipient_name_is_allowed_in_every_channel()
    {
        Problem(t => t with
        {
            InApp = new TextPair("Hallo $USER.firstName", "Willkommen zurück, $USER.firstName $USER.lastName"),
            Push = new TextPair("Hallo $USER.firstName", "Neue Mitteilung"),
            Email = new TextPair("Hallo $USER.firstName", "Guten Tag $USER.firstName $USER.lastName"),
        }).ShouldBe(TemplateProblem.None);
    }

    [Fact]
    public void Declared_class_1_data_is_allowed_in_every_channel_and_the_old_brace_form_still_works()
    {
        Problem(t => t with { Push = new TextPair("Titel", "{headline}"), Email = new TextPair("$headline", "$headline") }).ShouldBe(TemplateProblem.None);
    }

    [Fact]
    public void An_unknown_variable_is_refused_in_every_channel()
    {
        Problem(t => t with { InApp = new TextPair("Titel", "$USER.firstname") }).ShouldBe(TemplateProblem.UnknownVariable); // wrong case
        Problem(t => t with { Push = new TextPair("Titel", "$USER.middleName") }).ShouldBe(TemplateProblem.UnknownVariable);
        Problem(t => t with { Email = new TextPair("Titel", "$diagnosis") }).ShouldBe(TemplateProblem.UnknownVariable);
    }

    [Fact]
    public void Class_2_data_is_never_allowed_in_a_push_text_or_a_mail_subject()
    {
        Problem(t => HrNotice with { Push = new TextPair("Titel", "Wegen $reason") }).ShouldBe(TemplateProblem.SensitiveInPushOrSubject);
        Problem(t => HrNotice with { Push = new TextPair("$reason", "Text") }).ShouldBe(TemplateProblem.SensitiveInPushOrSubject);
        Problem(t => HrNotice with { Email = new TextPair("Wegen $reason", "Text") }).ShouldBe(TemplateProblem.SensitiveInPushOrSubject);
    }

    [Fact]
    public void Class_2_data_in_the_mail_text_needs_the_flag_of_the_template()
    {
        var sensitiveMail = new TextPair("Titel", "Wegen $reason");
        Problem(t => HrNotice with { Email = sensitiveMail }).ShouldBe(TemplateProblem.None);
        Problem(t => HrNotice with { Email = sensitiveMail, MailMayContainSensitive = false }).ShouldBe(TemplateProblem.SensitiveInMail);
    }

    [Fact]
    public void A_data_variable_may_not_take_the_name_of_a_recipient_variable() =>
        Problem(t => t with { Placeholders = new Dictionary<string, bool> { ["headline"] = false, ["USER.firstName"] = false } }).ShouldBe(TemplateProblem.ReservedName);

    [Theory]
    [InlineData("{")]
    [InlineData("a } b")]
    public void Stray_braces_are_refused_in_push_and_mail(string text)
    {
        Problem(t => t with { Push = new TextPair("Titel", text) }).ShouldBe(TemplateProblem.StrayBraces);
        Problem(t => t with { Email = new TextPair(text, "Text") }).ShouldBe(TemplateProblem.StrayBraces);
    }

    [Fact]
    public void A_dollar_sign_that_names_nothing_is_just_text() =>
        Problem(t => t with { Push = new TextPair("Preis 5 $", "ab 5$ oder $ 5") }).ShouldBe(TemplateProblem.None);

    [Fact]
    public void Empty_and_too_long_texts_are_refused()
    {
        Problem(t => t with { InApp = new TextPair(" ", "x") }).ShouldBe(TemplateProblem.EmptyText);
        Problem(t => t with { InApp = new TextPair("x", new string('a', TemplateRules.MaxBody + 1)) }).ShouldBe(TemplateProblem.TooLong);
    }

    // ---------------------------------------------------------------- rendering

    [Fact]
    public void The_name_is_filled_in_and_values_are_never_expanded_again()
    {
        var text = HrNotice.RenderInApp(
            new Dictionary<string, string> { ["DP.store"] = "0010 BaBu Oberhausen", ["DP.month"] = "November", ["DP.year"] = "2026", ["reason"] = "$USER.firstName" },
            new RecipientName("Hans", "Dampf"));

        text.Body.ShouldBe("Hallo Hans, Anpassung im DP 0010 BaBu Oberhausen November 2026 aufgrund von $USER.firstName nötig!");
    }

    [Fact]
    public void Without_a_known_name_the_variable_becomes_empty_instead_of_showing_the_variable() =>
        With(t => t with { InApp = new TextPair("Hallo", "Willkommen zurück, $USER.firstName!") }).RenderInApp(new Dictionary<string, string>(), null).Body.ShouldBe("Willkommen zurück, !");

    // ---------------------------------------------------------------- policy and delivery

    private static readonly IReadOnlyDictionary<string, string> HrData = new Dictionary<string, string>
    {
        ["DP.store"] = "0010 BaBu Oberhausen", ["DP.month"] = "November", ["DP.year"] = "2026", ["reason"] = "Krankheit",
    };

    [Fact]
    public void Class_2_data_by_mail_is_accepted_for_templates_that_allow_it_but_never_with_push()
    {
        NotificationPolicy.Check(Command(HrNotice.Key, [InApp, Email], HrData), Catalog()).Rejection.ShouldBeNull();
        NotificationPolicy.Check(Command(HrNotice.Key, [Email], HrData), Catalog()).Rejection.ShouldBeNull();
        NotificationPolicy.Check(Command(HrNotice.Key, [Push], HrData), Catalog()).Rejection.ShouldBe(Rejection.SensitiveDataOnNonInAppChannel);
        NotificationPolicy.Check(Command(HrNotice.Key, [InApp, Email, Push], HrData), Catalog()).Rejection.ShouldBe(Rejection.SensitiveDataOnNonInAppChannel);
    }

    [Fact]
    public void Class_2_data_by_mail_stays_refused_for_templates_without_the_flag() =>
        NotificationPolicy.Check(Command(channels: [Email]), Catalog()).Rejection.ShouldBe(Rejection.SensitiveDataOnNonInAppChannel);

    [Fact]
    public void Data_the_mail_text_uses_is_required_when_mail_is_requested()
    {
        var withoutReason = HrData.Where(d => d.Key != "reason").ToDictionary(d => d.Key, d => d.Value);

        NotificationPolicy.Check(Command(HrNotice.Key, [Email], withoutReason), Catalog()).Rejection.ShouldBe(Rejection.MissingData);
    }

    private readonly FakeNotificationStore _store = new();
    private readonly FakePreferences _preferences = new();
    private readonly FakePush _push = new();
    private readonly FakeEmail _email = new();
    private readonly FakeOverrides _overrides = new();
    private readonly FakeRecipients _names = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 3, 8, 0, 0, TimeSpan.Zero));

    private NotificationDispatcher Dispatcher() => new(Catalog(), _store, _preferences, _push, _email, _time, _names, _overrides);

    [Fact]
    public async Task Each_recipient_gets_the_text_with_their_own_name_in_every_channel()
    {
        _names.Names[Max] = new RecipientName("Hans", "Dampf");
        _names.Names[Sabine] = new RecipientName("Sabine", "Muster");

        await Dispatcher().DispatchAsync(Command(HrNotice.Key, [InApp, Email], HrData, [Max, Sabine]), CancellationToken.None);

        _store.InApp.Select(n => (n.EmployeeId, n.Body)).ShouldBe(
        [
            (Max, "Hallo Hans, Anpassung im DP 0010 BaBu Oberhausen November 2026 aufgrund von Krankheit nötig!"),
            (Sabine, "Hallo Sabine, Anpassung im DP 0010 BaBu Oberhausen November 2026 aufgrund von Krankheit nötig!"),
        ], ignoreOrder: true);
        _email.Sent.Select(m => (m.EmployeeId, m.Body)).ShouldBe(
        [
            (Max, "Hallo Hans, im DP 0010 BaBu Oberhausen November 2026 ist wegen Krankheit eine Anpassung nötig."),
            (Sabine, "Hallo Sabine, im DP 0010 BaBu Oberhausen November 2026 ist wegen Krankheit eine Anpassung nötig."),
        ], ignoreOrder: true);
    }

    [Fact]
    public async Task A_text_changed_by_HR_is_used_and_checked_again_at_delivery()
    {
        _names.Names[Max] = new RecipientName("Hans", "Dampf");
        _overrides.Items.Add(new TemplateOverride(News.Key, new TemplateTexts(null, new TextPair("Hallo $USER.firstName", "Neu: $headline"), null), _time.GetUtcNow(), Max));

        await Dispatcher().DispatchAsync(Command(News.Key, [Push], new Dictionary<string, string> { ["headline"] = "Neue Preise" }), CancellationToken.None);

        _push.Sent.ShouldHaveSingleItem().ShouldBe(new GenericMessage(Max, "Hallo Hans", "Neu: Neue Preise", "/absences/0197a000-0000-7000-8000-000000000201"));
    }

    [Fact]
    public async Task Where_the_recipients_may_receive_class_2_by_mail_HR_can_change_the_mail_text_but_not_the_subject()
    {
        var body = new TextPair("Dienstplan: Anpassung nötig", "Neu: Wegen $reason im DP $DP.store");

        (await Admin().SaveAsync(HrNotice.Key, new TemplateTexts(null, null, body), Max, CancellationToken.None)).Error.ShouldBe(TemplateAdminError.None);
        (await Admin().SaveAsync(HrNotice.Key, new TemplateTexts(null, null, body with { Title = "Anderer Betreff" }), Max, CancellationToken.None)).Error.ShouldBe(TemplateAdminError.SensitiveTextLocked);
        (await Admin().SaveAsync(HrNotice.Key, new TemplateTexts(null, new TextPair("T", "x"), null), Max, CancellationToken.None)).Error.ShouldBe(TemplateAdminError.SensitiveTextLocked);

        _names.Names[Max] = new RecipientName("Hans", "Dampf");
        await Dispatcher().DispatchAsync(Command(HrNotice.Key, [Email], HrData), CancellationToken.None);
        _email.Sent.Single().Body.ShouldBe("Neu: Wegen Krankheit im DP 0010 BaBu Oberhausen");
    }

    [Fact]
    public async Task A_faulty_change_falls_back_to_the_file_text_and_never_reaches_the_recipient()
    {
        // A stored text with a class 2 variable in the push text (e.g. the template was changed in a new version) must not be sent.
        _overrides.Items.Add(new TemplateOverride(HrNotice.Key, new TemplateTexts(null, new TextPair("Titel", "Wegen $reason"), null), _time.GetUtcNow(), Max));

        await Dispatcher().DispatchAsync(Command(HrNotice.Key, [InApp], HrData), CancellationToken.None);

        _store.InApp.Single().Body.ShouldContain("Krankheit"); // the in-app text of the file
        _push.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task Push_and_mail_texts_of_sensitive_templates_ignore_stored_changes()
    {
        _overrides.Items.Add(new TemplateOverride(HrNotice.Key, new TemplateTexts(null, new TextPair("Hallo $USER.firstName", "Geändert"), new TextPair("Geändert", "Geändert")), _time.GetUtcNow(), Max));

        await Dispatcher().DispatchAsync(Command(HrNotice.Key, [Email], HrData), CancellationToken.None);

        _email.Sent.Single().Title.ShouldBe("Dienstplan: Anpassung nötig");
    }

    // ---------------------------------------------------------------- maintenance by HR

    private TemplateAdminService Admin() => new(Catalog(), _overrides, _time);

    [Fact]
    public async Task HR_changes_a_text_and_only_what_differs_from_the_file_is_stored()
    {
        var result = await Admin().SaveAsync(News.Key, new TemplateTexts(new TextPair("Neuigkeit", "Hallo $USER.firstName: {headline}"), News.Push, null), Max, CancellationToken.None);

        result.Error.ShouldBe(TemplateAdminError.None);
        var stored = _overrides.Items.Single();
        (stored.Key, stored.ChangedBy, stored.ChangedAt).ShouldBe((News.Key, Max, _time.GetUtcNow()));
        stored.Texts.ShouldBe(new TemplateTexts(new TextPair("Neuigkeit", "Hallo $USER.firstName: {headline}"), null, null));
        (await Admin().GetAsync(News.Key, CancellationToken.None))!.Effective.InApp!.Body.ShouldBe("Hallo $USER.firstName: {headline}");
    }

    [Fact]
    public async Task Saving_the_texts_of_the_file_again_removes_the_change()
    {
        await Admin().SaveAsync(News.Key, new TemplateTexts(new TextPair("Anders", "Anders {headline}"), null, null), Max, CancellationToken.None);

        await Admin().SaveAsync(News.Key, new TemplateTexts(News.InApp, News.Push, News.Email), Max, CancellationToken.None);

        _overrides.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_text_that_breaks_a_rule_is_refused_with_the_problem_and_nothing_is_stored()
    {
        var unknown = await Admin().SaveAsync(News.Key, new TemplateTexts(new TextPair("T", "$diagnosis"), null, null), Max, CancellationToken.None);
        var sensitivePush = await Admin().SaveAsync(HrNotice.Key, new TemplateTexts(null, null, new TextPair("Wegen $reason", "x")), Max, CancellationToken.None);

        (unknown.Error, unknown.Problem).ShouldBe((TemplateAdminError.Invalid, TemplateProblem.UnknownVariable));
        sensitivePush.Error.ShouldBe(TemplateAdminError.SensitiveTextLocked); // sensitive template: push and mail are fixed
        (await Admin().SaveAsync("workforce.unknown.hr", new TemplateTexts(null, null, null), Max, CancellationToken.None)).Error.ShouldBe(TemplateAdminError.UnknownTemplate);
        _overrides.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Class_2_in_a_push_text_is_refused_also_for_templates_that_are_not_locked()
    {
        var open = News with { Placeholders = new Dictionary<string, bool> { ["headline"] = false, ["secret"] = true }, InApp = new TextPair("N", "$headline $secret") };
        var admin = new TemplateAdminService(new TemplateCatalog([open]), _overrides, _time);

        var result = await admin.SaveAsync(open.Key, new TemplateTexts(null, new TextPair("T", "$secret"), null), Max, CancellationToken.None);

        (result.Error, result.Problem).ShouldBe((TemplateAdminError.Invalid, TemplateProblem.SensitiveInPushOrSubject));
    }

    [Fact]
    public async Task The_view_lists_defaults_effective_texts_and_the_usable_variables()
    {
        await Admin().SaveAsync(News.Key, new TemplateTexts(new TextPair("Neu", "Neu {headline}"), null, null), Max, CancellationToken.None);

        var view = (await Admin().GetAsync(News.Key, CancellationToken.None))!;

        (view.Customized, view.Default.InApp, view.Effective.InApp!.Title).ShouldBe((true, News.InApp, "Neu"));
        view.Variables.Select(v => (v.Name, v.Sensitive, v.FromRecipient)).ShouldBe([("USER.firstName", false, true), ("USER.lastName", false, true), ("headline", false, false)]);
        (await Admin().GetAsync("nope.nope.nope", CancellationToken.None)).ShouldBeNull();
        (await Admin().ListAsync(CancellationToken.None)).Select(v => v.Key).ShouldBe(new[] { HrNotice.Key, News.Key, SickNote.Key }.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Reset_returns_to_the_file_text()
    {
        await Admin().SaveAsync(News.Key, new TemplateTexts(new TextPair("Neu", "Neu {headline}"), null, null), Max, CancellationToken.None);

        (await Admin().ResetAsync(News.Key, CancellationToken.None)).ShouldBe(TemplateAdminError.None);

        _overrides.Items.ShouldBeEmpty();
        (await Admin().ResetAsync("nope.nope.nope", CancellationToken.None)).ShouldBe(TemplateAdminError.UnknownTemplate);
    }

    [Fact]
    public void The_preview_shows_the_texts_with_an_example_name_and_stores_nothing()
    {
        var preview = Admin().Preview(News.Key, new TemplateTexts(new TextPair("Hallo $USER.firstName", "Neu: {headline}"), null, null), new Dictionary<string, string> { ["headline"] = "Preise" });

        preview.Result.Error.ShouldBe(TemplateAdminError.None);
        preview.Rendered!.InApp!.ShouldBe(new TextPair("Hallo Hans", "Neu: Preise"));
        Admin().Preview(News.Key, new TemplateTexts(new TextPair("T", "{x}"), null, null), null).Result.Problem.ShouldBe(TemplateProblem.UnknownVariable);
        preview.Rendered.Push!.Body.ShouldBe(News.Push.Body);
        _overrides.Items.ShouldBeEmpty();
    }

    [Fact]
    public void The_preview_without_sample_values_shows_the_variable_in_brackets() =>
        Admin().Preview(News.Key, new TemplateTexts(null, null, null), null).Rendered!.InApp!.Body.ShouldBe("[headline]");
}
