using Notifications.Core.Templates;

namespace Notifications.Core.Tests;

public class TemplateCatalogTests
{
    private static Template With(Func<Template, Template> change) => change(Samples.SickNote);

    [Fact]
    public void Valid_templates_are_found_by_key()
    {
        var catalog = Samples.Catalog();

        catalog.TryGet("portal.news.employee", out var template).ShouldBeTrue();
        template!.Mandatory.ShouldBeFalse();
        catalog.TryGet("portal.unknown.employee", out _).ShouldBeFalse();
        catalog.Keys.Count.ShouldBe(3);
    }

    [Theory]
    [InlineData("sick-note")]
    [InlineData("Workforce.Sick-Note.Store-Manager")]
    [InlineData("workforce.sick note.store-manager")]
    [InlineData("a.b.c.d")]
    [InlineData("")]
    public void Key_follows_service_occasion_recipient_role(string key) =>
        Should.Throw<TemplateException>(() => new TemplateCatalog([With(t => t with { Key = key })]));

    [Theory]
    [InlineData("{employeeName} ist krank")]
    [InlineData("Details: {from}")]
    [InlineData("{")]
    [InlineData("}")]
    public void Push_and_mail_texts_are_generic_and_may_not_contain_placeholders(string text)
    {
        Should.Throw<TemplateException>(() => new TemplateCatalog([With(t => t with { Push = new TextPair("Titel", text) })]));
        Should.Throw<TemplateException>(() => new TemplateCatalog([With(t => t with { Email = new TextPair(text, "Text") })]));
    }

    [Fact]
    public void In_app_text_may_only_use_declared_placeholders() =>
        Should.Throw<TemplateException>(() => new TemplateCatalog([With(t => t with { InApp = new TextPair("Titel", "{diagnosis}") })]));

    [Fact]
    public void Duplicate_keys_are_refused() =>
        Should.Throw<TemplateException>(() => new TemplateCatalog([Samples.SickNote, Samples.SickNote]));

    [Fact]
    public void Texts_must_not_be_empty() =>
        Should.Throw<TemplateException>(() => new TemplateCatalog([With(t => t with { Push = new TextPair("", "x") })]));

    [Fact]
    public void Parses_a_template_file()
    {
        const string json = """
            {
              "key": "workforce.sick-note.store-manager",
              "mandatory": true,
              "placeholders": { "employeeName": "sensitive", "from": "sensitive", "headline": "public" },
              "inApp": { "title": "Krankmeldung", "body": "{employeeName} ab {from}" },
              "push": { "title": "Neue Mitteilung im Portal", "body": "Öffnen." },
              "email": { "title": "Neue Mitteilung im Portal", "body": "Es liegt etwas bereit." }
            }
            """;

        var template = TemplateParser.Parse(json);

        template.Key.ShouldBe("workforce.sick-note.store-manager");
        template.Mandatory.ShouldBeTrue();
        template.Placeholders["employeeName"].ShouldBeTrue();
        template.Placeholders["headline"].ShouldBeFalse();
        template.InApp.Body.ShouldBe("{employeeName} ab {from}");
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("{}")]
    [InlineData("""{"key":"a.b.c","placeholders":{"x":"maybe"},"inApp":{"title":"t","body":"b"},"push":{"title":"t","body":"b"},"email":{"title":"t","body":"b"}}""")]
    public void Unreadable_or_incomplete_files_fail_with_a_clear_error(string json) =>
        Should.Throw<TemplateException>(() => TemplateParser.Parse(json));
}
