using System.Net;
using System.Net.Http.Json;
using BcIntegration.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Api;
using Notifications.Contracts;
using Notifications.Infrastructure;
using Notifications.Infrastructure.Delivery;
using Shared.Infrastructure.Messaging;
using Shared.Messaging;
using Shared.Testing;
using static Notifications.Api.Tests.NotificationsApi;

namespace Notifications.Api.Tests;

public sealed class TemplateEndpointTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Free = "workforce.employee-change-decision.employee";
    private const string HrSickness = "workforce.shift-uncovered-sickness.hr";
    private const string SickNote = "workforce.sick-note.store-manager";

    private static readonly IReadOnlyDictionary<string, string> HrData = new Dictionary<string, string>
    {
        ["DP.store"] = "0010 BaBu Oberhausen", ["DP.month"] = "November", ["DP.year"] = "2026",
    };

    private static EmployeeChanged Changed(Guid id, string name, DateTimeOffset at, DateOnly? exit = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), at, id, "1001", name, null, null, null, null, null, exit);

    private Task<NotificationsApi> Start() => StartAsync(postgres);

    // ---------------------------------------------------------------- who may maintain

    [Fact]
    public async Task Only_holders_of_the_privilege_see_or_change_the_texts()
    {
        await using var api = await Start();
        var body = new SaveTemplateRequest(new TextDto("T", "Text"), null, null);

        foreach (var person in new Guid?[] { Erika, Max, null })
        {
            var expected = person is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
            (await api.SendAsync(HttpMethod.Get, "/api/notification-templates", person)).StatusCode.ShouldBe(expected);
            (await api.SendAsync(HttpMethod.Get, $"/api/notification-templates/{Free}", person)).StatusCode.ShouldBe(expected);
            (await api.SendAsync(HttpMethod.Put, $"/api/notification-templates/{Free}", person, body)).StatusCode.ShouldBe(expected);
            (await api.SendAsync(HttpMethod.Delete, $"/api/notification-templates/{Free}", person)).StatusCode.ShouldBe(expected);
            (await api.SendAsync(HttpMethod.Post, $"/api/notification-templates/{Free}/preview", person, new PreviewTemplateRequest(null, null, null, null))).StatusCode.ShouldBe(expected);
        }
    }

    [Fact]
    public async Task The_list_shows_every_template_with_its_rules_and_usable_variables()
    {
        await using var api = await Start();

        var list = await api.GetAsync<List<TemplateDto>>("/api/notification-templates", Hanna);

        list.Select(t => t.Key).ShouldContain(Free);
        var sickness = list.Single(t => t.Key == HrSickness);
        (sickness.SensitiveText, sickness.MailMayContainSensitive, sickness.Customized).ShouldBe((true, true, false));
        sickness.Variables.Select(v => (v.Name, v.Class, v.Source)).ShouldBe([("USER.firstName", 1, "Recipient"), ("USER.lastName", 1, "Recipient"), ("DP.month", 1, "Sender"), ("DP.store", 1, "Sender"), ("DP.year", 1, "Sender")]);
        sickness.Default.InApp!.Body.ShouldBe("Anpassung im DP $DP.store $DP.month $DP.year aufgrund von Krankheit nötig!");
        list.Single(t => t.Key == SickNote).Variables.Where(v => v.Class == 2).Select(v => v.Name).ShouldBe(["employeeName", "from", "to"], ignoreOrder: true);
    }

    // ---------------------------------------------------------------- change, preview, reset

    [Fact]
    public async Task HR_changes_the_text_with_the_recipients_name_and_the_recipient_sees_it_with_their_own_first_name()
    {
        await using var api = await Start();
        await api.DeliverMessageAsync(Changed(Max, "Hans Dampf", DateTimeOffset.UnixEpoch));
        var change = new SaveTemplateRequest(new TextDto("Entscheidung", "Hallo $USER.firstName, Ihr Antrag wurde bearbeitet."), new TextDto("Hallo $USER.firstName", "Neue Mitteilung im Portal"), null);

        (await api.SendAsync(HttpMethod.Put, $"/api/notification-templates/{Free}", Hanna, change)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var view = await api.GetAsync<TemplateDto>($"/api/notification-templates/{Free}", Hanna);
        (view.Customized, view.Effective.InApp!.Body, view.Default.InApp!.Body).ShouldBe((true, "Hallo $USER.firstName, Ihr Antrag wurde bearbeitet.", view.Default.InApp.Body));
        view.Effective.Email.ShouldBe(view.Default.Email); // untouched texts follow the file

        await api.DeliverAsync(Command(template: Free, channels: [NotificationChannel.InApp, NotificationChannel.Push], data: new Dictionary<string, string>()));

        (await api.GetAsync<List<NotificationDto>>("/api/me/notifications", Max)).Single().Body.ShouldBe("Hallo Hans, Ihr Antrag wurde bearbeitet.");
        api.Services.GetRequiredService<SentMessages>().Items.Single(i => i.Channel == "Push").Message.Title.ShouldBe("Hallo Hans");
    }

    [Fact]
    public async Task Rules_are_enforced_on_save_unknown_variables_class_2_in_the_wrong_place_and_fixed_texts()
    {
        await using var api = await Start();

        async Task<(HttpStatusCode, string?)> Put(string key, SaveTemplateRequest body)
        {
            using var response = await api.SendAsync(HttpMethod.Put, $"/api/notification-templates/{key}", Hanna, body);
            return (response.StatusCode, response.StatusCode == HttpStatusCode.NoContent ? null : (await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("title").GetString());
        }

        (await Put(Free, new SaveTemplateRequest(new TextDto("T", "$USER.nickname"), null, null))).ShouldBe((HttpStatusCode.UnprocessableEntity, "UnknownVariable"));
        (await Put(Free, new SaveTemplateRequest(new TextDto(" ", "Text"), null, null))).ShouldBe((HttpStatusCode.UnprocessableEntity, "EmptyText"));
        // Sick note (class 2 in the text): push and mail are fixed.
        (await Put(SickNote, new SaveTemplateRequest(null, new TextDto("T", "x"), null))).ShouldBe((HttpStatusCode.Conflict, "SensitiveTextLocked"));
        (await Put(SickNote, new SaveTemplateRequest(null, null, new TextDto("T", "x")))).ShouldBe((HttpStatusCode.Conflict, "SensitiveTextLocked"));
        (await Put(SickNote, new SaveTemplateRequest(new TextDto("Krankmeldung", "$employeeName ab $from krank."), null, null))).ShouldBe((HttpStatusCode.NoContent, null)); // in-app may use class 2
        (await Put("workforce.unknown.hr", new SaveTemplateRequest(null, null, null))).ShouldBe((HttpStatusCode.NotFound, "UnknownTemplate"));
        (await api.GetAsync<TemplateDto>($"/api/notification-templates/{Free}", Hanna)).Customized.ShouldBeFalse();
    }

    [Fact]
    public async Task For_HR_the_mail_text_may_carry_class_2_but_push_and_subject_stay_fixed()
    {
        await using var api = await Start();
        await api.DeliverMessageAsync(Changed(Max, "Hans Dampf", DateTimeOffset.UnixEpoch));
        var mail = new TextDto("Dienstplan: Anpassung nötig", "Hallo $USER.firstName, im DP $DP.store ($DP.month $DP.year) ist wegen Krankheit eine Anpassung nötig.");

        (await api.SendAsync(HttpMethod.Put, $"/api/notification-templates/{HrSickness}", Hanna, new SaveTemplateRequest(null, null, mail))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await api.SendAsync(HttpMethod.Put, $"/api/notification-templates/{HrSickness}", Hanna, new SaveTemplateRequest(null, null, mail with { Title = "Wegen Krankheit" }))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await api.SendAsync(HttpMethod.Put, $"/api/notification-templates/{HrSickness}", Hanna, new SaveTemplateRequest(null, new TextDto("Neu", "Neu"), null))).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        (await api.DeliverAsync(Command(template: HrSickness, channels: [NotificationChannel.InApp, NotificationChannel.Email], data: HrData))).Outcome.ShouldBe(DispatchOutcome.Completed);
        api.Services.GetRequiredService<SentMessages>().Items.Single().Message.Body.ShouldBe("Hallo Hans, im DP 0010 BaBu Oberhausen (November 2026) ist wegen Krankheit eine Anpassung nötig.");
    }

    [Fact]
    public async Task Reset_returns_to_the_text_of_the_file()
    {
        await using var api = await Start();
        await api.SendAsync(HttpMethod.Put, $"/api/notification-templates/{Free}", Hanna, new SaveTemplateRequest(new TextDto("Neu", "Neu"), null, null));

        (await api.SendAsync(HttpMethod.Delete, $"/api/notification-templates/{Free}", Hanna)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var view = await api.GetAsync<TemplateDto>($"/api/notification-templates/{Free}", Hanna);
        (view.Customized, view.Effective).ShouldBe((false, view.Default));
        (await api.SendAsync(HttpMethod.Delete, "/api/notification-templates/workforce.unknown.hr", Hanna)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await api.SendAsync(HttpMethod.Get, "/api/notification-templates/workforce.unknown.hr", Hanna)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_preview_shows_the_text_for_Hans_Dampf_and_changes_nothing()
    {
        await using var api = await Start();

        using var response = await api.SendAsync(HttpMethod.Post, $"/api/notification-templates/{HrSickness}/preview", Hanna,
            new PreviewTemplateRequest(new TextDto("Hallo $USER.firstName", "DP $DP.store $DP.month"), null, null, new Dictionary<string, string> { ["DP.store"] = "0010 BaBu Oberhausen" }));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var preview = (await response.Content.ReadFromJsonAsync<TemplateTextsDto>())!;
        preview.InApp.ShouldBe(new TextDto("Hallo Hans", "DP 0010 BaBu Oberhausen [DP.month]"));
        (await api.GetAsync<TemplateDto>($"/api/notification-templates/{HrSickness}", Hanna)).Customized.ShouldBeFalse();
        (await api.SendAsync(HttpMethod.Post, $"/api/notification-templates/{Free}/preview", Hanna, new PreviewTemplateRequest(new TextDto("T", "$bad"), null, null, null))).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    // ---------------------------------------------------------------- delivery rules

    [Fact]
    public async Task Class_2_data_by_mail_reaches_HR_templates_but_never_push_and_never_other_templates()
    {
        await using var api = await Start();
        var withReason = new Dictionary<string, string>(HrData); // class 1 only: the files declare no class 2 variable for HR yet

        (await api.DeliverAsync(Command(template: HrSickness, channels: [NotificationChannel.InApp, NotificationChannel.Email], data: withReason))).Outcome.ShouldBe(DispatchOutcome.Completed);
        (await api.DeliverAsync(Command(template: HrSickness, channels: [NotificationChannel.Push], data: withReason))).Outcome.ShouldBe(DispatchOutcome.Completed); // the push text has no class 2 data: it is generic
        (await api.DeliverAsync(Command(template: SickNote, channels: [NotificationChannel.Email]))).Reason.ShouldBe("SensitiveDataOnNonInAppChannel");
        (await api.DeliverAsync(Command(template: SickNote, channels: [NotificationChannel.Push]))).Reason.ShouldBe("SensitiveDataOnNonInAppChannel");
    }

    // ---------------------------------------------------------------- the name copy

    [Fact]
    public async Task The_name_copy_follows_EmployeeChanged_and_ignores_older_messages()
    {
        await using var api = await Start();
        var t0 = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

        await api.DeliverMessageAsync(Changed(Max, "Dampf, Hans", t0));
        await api.DeliverMessageAsync(Changed(Max, "Hans Peter Dampf", t0.AddDays(1), new DateOnly(2027, 2, 28)));
        await api.DeliverMessageAsync(Changed(Max, "Alter Name", t0.AddHours(-1)));

        using var scope = api.Services.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().EmployeeNames.AsNoTracking().SingleAsync();
        (row.FirstName, row.LastName, row.ExitDate).ShouldBe(("Hans", "Peter Dampf", new DateOnly(2027, 2, 28)));
    }

    [Fact]
    public async Task Erasure_removes_the_name_and_anonymises_the_author_of_text_changes()
    {
        await using var api = await Start();
        await api.DeliverMessageAsync(Changed(Hanna, "Hanna Hr", DateTimeOffset.UnixEpoch));
        await api.DeliverMessageAsync(Changed(Max, "Hans Dampf", DateTimeOffset.UnixEpoch));
        await api.SendAsync(HttpMethod.Put, $"/api/notification-templates/{Free}", Hanna, new SaveTemplateRequest(new TextDto("Neu", "Neu"), null, null));

        var erasure = new EmployeeDataErasureRequested(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UnixEpoch, Hanna);
        (await api.DeliverMessageAsync(erasure)).Outcome.ShouldBe(DispatchOutcome.Completed);

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        (await db.EmployeeNames.Select(n => n.EmployeeId).ToListAsync()).ShouldBe([Max]);
        (await db.TemplateOverrides.AsNoTracking().SingleAsync()).ChangedBy.ShouldBe(Guid.Empty);
    }
}
