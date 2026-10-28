using Notifications.Core.Delivery;
using Notifications.Infrastructure.Templates;

namespace Notifications.Api.Tests;

public class TemplateFilesTests
{
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "Templates");

    [Fact]
    public void Shipped_templates_load_and_follow_the_naming_scheme()
    {
        var catalog = TemplateLoader.Load(Folder);

        catalog.Keys.Order().ShouldBe(
        ["workforce.employee-change-decision.employee", "workforce.employee-change-request.hr", "workforce.shift-uncovered-absence.hr", "workforce.shift-uncovered-sickness.hr", "workforce.sick-note-certificate-rejected.employee", "workforce.sick-note-certificate-reminder.employee", "workforce.sick-note.store-manager", "workforce.vacation-decision.requester", "workforce.vacation-request.supervisor"]);
        catalog.TryGet("workforce.sick-note.store-manager", out var sick).ShouldBeTrue();
        sick!.Mandatory.ShouldBeTrue();
        sick.Placeholders.Values.ShouldAllBe(sensitive => sensitive); // everything about an absence is class 2
    }

    [Fact]
    public void Push_and_mail_texts_of_all_shipped_templates_are_identical_and_generic()
    {
        var all = TemplateLoader.Load(Folder).All;

        all.Select(t => t.Push.Title).Distinct().ShouldHaveSingleItem().ShouldBe("Neue Mitteilung im Portal");
        all.SelectMany(t => new[] { t.Push.Body, t.Email.Body, t.Email.Title }).ShouldAllBe(text => !text.Contains('{', StringComparison.Ordinal));
    }

    [Fact]
    public void A_faulty_or_misnamed_file_stops_the_load()
    {
        var folder = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(folder, "portal.news.employee.json"),
            """{"key":"portal.other.employee","inApp":{"title":"t","body":"b"},"push":{"title":"t","body":"b"},"email":{"title":"t","body":"b"}}""");
        try
        {
            Should.Throw<Notifications.Core.Templates.TemplateException>(() => TemplateLoader.Load(folder));
            Should.Throw<Notifications.Core.Templates.TemplateException>(() => TemplateLoader.Load(Path.Combine(folder, "missing")));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void The_message_handed_to_push_and_mail_adapters_cannot_carry_data()
    {
        typeof(GenericMessage).GetProperties().Select(p => p.Name).Order().ShouldBe(["Body", "DeepLink", "EmployeeId", "Title"]);
        typeof(GenericMessage).GetProperties().Where(p => p.Name is "Body" or "Title" or "DeepLink").ShouldAllBe(p => p.PropertyType == typeof(string));
    }
}
