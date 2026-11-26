using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Notifications.Contracts;
using Portal.Core;
using Portal.Core.Logbook;
using Portal.Infrastructure;
using Portal.Infrastructure.Logbook;
using Portal.Infrastructure.Messaging;
using Shared.Messaging;
using Shared.Testing;
using static Portal.Api.Tests.PortalApi;

namespace Portal.Api.Tests;

/// <summary>
/// The notifications of the Berichtsheft (decision 49) and what belongs to them: escalating to the district leaders, reminding the store managers by hand,
/// the list of missing reports, the settings of the automatic reminder and the reminder itself. Every notification carries nothing from the report.
/// Clock: Friday 2026-10-23, 12:00 UTC (14:00 local), current week 2026-10-19.
/// </summary>
public sealed class LogbookNotificationEndpointTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string ThisWeek = "2026-10-19";
    private const string SecretText = "GEHEIMER-BERICHTSTEXT";

    private static readonly Guid DistrictSouth = Guid.Parse("00000000-0000-0000-0000-0000000000d1");
    private static readonly Guid RegionAugsburg = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid RegionMunich = Guid.Parse("00000000-0000-0000-0000-0000000000a2");
    private static readonly Guid Mia = Guid.Parse("00000000-0000-0000-0000-0000000000a9");
    private static readonly Guid Nils = Guid.Parse("00000000-0000-0000-0000-0000000000aa");
    private static readonly Guid Fiona = Guid.Parse("00000000-0000-0000-0000-0000000000ab");

    // Store managers: Erika (A), Fiona (B), Hanna (C). Regional leaders: It and Mia (Augsburg: A, B), Nils (Munich: C). District leader: Revisor. Ben leads nothing.
    private static async Task<PortalApi> StartUpAsync(PostgresFixture postgres)
    {
        var api = await StartAsync(postgres);
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            db.People.AddRange(
                new PersonRecord { EmployeeId = Mia, Name = "Mia Region", HomeStoreId = StoreA },
                new PersonRecord { EmployeeId = Nils, Name = "Nils Nachbar", HomeStoreId = StoreC },
                new PersonRecord { EmployeeId = Fiona, Name = "Fiona Filiale", HomeStoreId = StoreB });
            await db.SaveChangesAsync();
        }

        var structure = new
        {
            districts = new object[] { new { id = DistrictSouth, name = "Süd", leaderIds = new[] { Revisor } } },
            regions = new object[]
            {
                new { id = RegionAugsburg, districtId = DistrictSouth, name = "Augsburg", leaderIds = new[] { It, Mia } },
                new { id = RegionMunich, districtId = DistrictSouth, name = "München", leaderIds = new[] { Nils } },
            },
            stores = new object[]
            {
                new { storeId = StoreA, districtId = DistrictSouth, regionId = (Guid?)RegionAugsburg },
                new { storeId = StoreB, districtId = DistrictSouth, regionId = (Guid?)RegionAugsburg },
                new { storeId = StoreC, districtId = DistrictSouth, regionId = (Guid?)RegionMunich },
            },
        };
        using var response = await api.SendAsync(HttpMethod.Put, "/api/logbook/structure", Hanna, structure);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        api.Grant(Erika, new TestGrant(PortalPrivileges.LogbookWrite, [StoreA]));
        api.Grant(Fiona, new TestGrant(PortalPrivileges.LogbookWrite, [StoreB]));
        foreach (var leader in new[] { It, Mia, Nils, Revisor, Ben })
        {
            api.Grant(leader, TestGrant.Everywhere(PortalPrivileges.LogbookReview));
        }

        // Hanna: administration (structure, reminder settings) and store manager of C only.
        api.Grant(Hanna, TestGrant.Everywhere(PortalPrivileges.LogbookManageStructure), TestGrant.Everywhere(PortalPrivileges.LogbookManageReminders), new TestGrant(PortalPrivileges.LogbookWrite, [StoreC]));
        return api;
    }

    private static LogbookSection Section(int index) => LogbookTemplate.Default.Sections[index];

    private static Dictionary<string, object> Complete(string text = SecretText) => new()
    {
        [Section(0).Id.ToString()] = new { optionIds = new[] { Section(0).Options[0].Id }, text = (string?)null },
        [Section(1).Id.ToString()] = new { optionIds = new[] { Section(1).Options[0].Id }, text = (string?)null },
        [Section(3).Id.ToString()] = new { optionIds = Array.Empty<Guid>(), text = (string?)text },
        [Section(5).Id.ToString()] = new { optionIds = new[] { Section(5).Options[0].Id }, text = (string?)null },
    };

    private static async Task<Guid> SubmittedAsync(PortalApi api, Guid manager, Guid store)
    {
        (await api.SendAsync(HttpMethod.Put, $"/api/logbook/reports/{store}/{ThisWeek}", manager, new { answers = Complete() })).StatusCode.ShouldBe(HttpStatusCode.OK);
        using var response = await api.SendAsync(HttpMethod.Post, $"/api/logbook/reports/{store}/{ThisWeek}/submit", manager);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await DataAsync(response)).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> PostAsync(PortalApi api, Guid? person, string url, object? body = null, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var response = await api.SendAsync(HttpMethod.Post, url, person, body);
        response.StatusCode.ShouldBe(expected, await response.Content.ReadAsStringAsync());
        return expected == HttpStatusCode.OK ? await DataAsync(response) : default;
    }

    /// <summary>The notifications sent so far (from the outbox), oldest first.</summary>
    private static async Task<List<(SendNotification Message, string Payload)>> SentAsync(PortalApi api, string? template = null)
    {
        var all = (await api.OutboxAsync()).Where(m => m.MessageType == nameof(SendNotification)).Select(m => (Message: MessageJson.Deserialize<SendNotification>(m.Payload), m.Payload));
        return [.. all.Where(n => template is null || n.Message.TemplateKey == template)];
    }

    private static Guid[] Recipients((SendNotification Message, string Payload) n) => [.. n.Message.RecipientEmployeeIds.Order()];

    // ------------------------------------------------------------------ what is notified, and to whom

    [Fact]
    public async Task Handing_in_notifies_the_regional_leaders_with_nothing_from_the_report()
    {
        await using var api = await StartUpAsync(postgres);

        var report = await SubmittedAsync(api, Erika, StoreA);

        var sent = (await SentAsync(api, LogbookNotifier.Submitted)).ShouldHaveSingleItem();
        Recipients(sent).ShouldBe([.. new[] { It, Mia }.Order()]);
        sent.Message.Channels.ShouldBe([NotificationChannel.InApp, NotificationChannel.Push]);
        sent.Message.DeepLink.ShouldBe($"/logbook/reports/{report}");
        sent.Message.Data.ShouldBeEmpty();
        sent.Payload.ShouldNotContain(SecretText); // class 2: the content never leaves the Portal
        sent.Payload.ShouldNotContain("Store A");
    }

    [Fact]
    public async Task Handing_in_also_tells_the_district_leaders_quietly_as_an_info_without_push()
    {
        await using var api = await StartUpAsync(postgres);

        var report = await SubmittedAsync(api, Erika, StoreA);

        var info = (await SentAsync(api, LogbookNotifier.SubmittedDistrict)).ShouldHaveSingleItem();
        Recipients(info).ShouldBe([Revisor]);
        info.Message.Channels.ShouldBe([NotificationChannel.InApp]);
        info.Message.DeepLink.ShouldBe($"/logbook/reports/{report}");
        info.Message.Data.ShouldBeEmpty();
        info.Payload.ShouldNotContain(SecretText);
    }

    [Fact]
    public async Task A_second_hand_in_and_a_draft_notify_nobody()
    {
        await using var api = await StartUpAsync(postgres);
        (await api.SendAsync(HttpMethod.Put, $"/api/logbook/reports/{StoreA}/{ThisWeek}", Erika, new { answers = Complete() })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await SentAsync(api)).ShouldBeEmpty(); // a draft is private

        await SubmittedAsync(api, Erika, StoreA);
        await PostAsync(api, Erika, $"/api/logbook/reports/{StoreA}/{ThisWeek}/submit");

        (await SentAsync(api, LogbookNotifier.Submitted)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_reply_of_the_regional_leader_goes_to_the_store_manager_and_a_reply_of_the_district_leader_to_the_regional_leaders_internal_comments_to_nobody()
    {
        await using var api = await StartUpAsync(postgres);
        var report = await SubmittedAsync(api, Erika, StoreA);

        await PostAsync(api, It, $"/api/logbook/reports/{report}/comments", new { kind = "reply", text = SecretText });
        await PostAsync(api, Revisor, $"/api/logbook/reports/{report}/comments", new { kind = "reply", text = SecretText });
        await PostAsync(api, It, $"/api/logbook/reports/{report}/comments", new { kind = "internal", text = SecretText });
        await PostAsync(api, Revisor, $"/api/logbook/reports/{report}/comments", new { kind = "internal", text = SecretText });

        var toStore = (await SentAsync(api, LogbookNotifier.ReplyToStore)).ShouldHaveSingleItem();
        Recipients(toStore).ShouldBe([Erika]);
        var toRegional = (await SentAsync(api, LogbookNotifier.ReplyToRegional)).ShouldHaveSingleItem();
        Recipients(toRegional).ShouldBe([.. new[] { It, Mia }.Order()]); // the district leader answers the regional leaders: the store manager does not hear it directly
        (await SentAsync(api)).ShouldAllBe(n => !n.Payload.Contains(SecretText));
        (await SentAsync(api)).Count(n => n.Message.TemplateKey is LogbookNotifier.ReplyToStore or LogbookNotifier.ReplyToRegional).ShouldBe(2); // nothing for the internal comments
    }

    [Fact]
    public async Task The_regional_leader_who_replies_is_not_notified_about_the_own_action_and_a_district_leader_who_replies_when_there_is_no_regional_leader_notifies_nobody()
    {
        await using var api = await StartUpAsync(postgres);
        var report = await SubmittedAsync(api, Erika, StoreA);

        await PostAsync(api, Mia, $"/api/logbook/reports/{report}/comments", new { kind = "reply", text = "x" });

        (await SentAsync(api, LogbookNotifier.ReplyToStore)).ShouldHaveSingleItem().Message.RecipientEmployeeIds.ShouldNotContain(Mia);
        (await SentAsync(api, LogbookNotifier.Submitted)).ShouldHaveSingleItem().Message.RecipientEmployeeIds.ShouldNotContain(Erika);
    }

    [Fact]
    public async Task Following_up_notifies_the_regional_leaders()
    {
        await using var api = await StartUpAsync(postgres);
        var report = await SubmittedAsync(api, Erika, StoreA);

        await PostAsync(api, Erika, $"/api/logbook/reports/{report}/follow-up", new { note = SecretText });

        var sent = (await SentAsync(api, LogbookNotifier.FollowedUp)).ShouldHaveSingleItem();
        Recipients(sent).ShouldBe([.. new[] { It, Mia }.Order()]);
        sent.Payload.ShouldNotContain(SecretText);
        sent.Message.DeepLink.ShouldBe($"/logbook/reports/{report}");
    }

    [Fact]
    public async Task Every_notification_has_its_own_id_that_is_always_the_same_for_the_same_event()
    {
        await using var api = await StartUpAsync(postgres);
        var report = await SubmittedAsync(api, Erika, StoreA);
        await PostAsync(api, It, $"/api/logbook/reports/{report}/comments", new { kind = "reply", text = "x" });
        await PostAsync(api, It, $"/api/logbook/reports/{report}/comments", new { kind = "reply", text = "y" });

        var ids = (await SentAsync(api)).Select(n => n.Message.NotificationId).ToList();

        ids.Distinct().Count().ShouldBe(ids.Count); // two replies, two different notifications
        ids.ShouldAllBe(id => id != Guid.Empty);
    }

    // ------------------------------------------------------------------ escalating

    [Fact]
    public async Task A_regional_leader_escalates_once_and_the_district_leaders_are_told_the_store_manager_does_not_see_it()
    {
        await using var api = await StartUpAsync(postgres);
        var report = await SubmittedAsync(api, Erika, StoreA);

        var escalated = await PostAsync(api, It, $"/api/logbook/reports/{report}/escalate", new { note = "Brauche Hilfe bei der Kasse." });

        escalated.GetProperty("escalatedAt").GetDateTimeOffset().ShouldBe(Start);
        escalated.GetProperty("escalatedByName").GetString().ShouldBe("Ingo IT");
        escalated.GetProperty("escalationNote").GetString().ShouldBe("Brauche Hilfe bei der Kasse.");
        var sent = (await SentAsync(api, LogbookNotifier.Escalated)).ShouldHaveSingleItem();
        Recipients(sent).ShouldBe([Revisor]);
        sent.Payload.ShouldNotContain("Kasse");

        await PostAsync(api, It, $"/api/logbook/reports/{report}/escalate", new { note = "Nochmal" }, HttpStatusCode.Conflict); // once per report
        await PostAsync(api, Revisor, $"/api/logbook/reports/{report}/escalate", new { note = "x" }, HttpStatusCode.Conflict); // the district leader is the top
        (await SentAsync(api, LogbookNotifier.Escalated)).Count.ShouldBe(1);

        foreach (var person in new[] { Revisor, Mia })
        {
            using var response = await api.SendAsync(HttpMethod.Get, $"/api/logbook/reports/{report}", person);
            (await DataAsync(response)).GetProperty("escalatedAt").ValueKind.ShouldBe(JsonValueKind.String);
        }

        using var asStoreManager = await api.SendAsync(HttpMethod.Get, $"/api/logbook/reports/{report}", Erika);
        var seenByStore = await DataAsync(asStoreManager);
        seenByStore.GetProperty("escalatedAt").ValueKind.ShouldBe(JsonValueKind.Null);
        seenByStore.GetProperty("escalationNote").ValueKind.ShouldBe(JsonValueKind.Null);
        using var list = await api.SendAsync(HttpMethod.Get, "/api/logbook/reports", Revisor);
        (await DataAsync(list)).EnumerateArray().Single().GetProperty("escalated").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Escalating_needs_a_leader_of_the_store_a_handed_in_report_and_a_short_note()
    {
        await using var api = await StartUpAsync(postgres);
        var report = await SubmittedAsync(api, Erika, StoreA);

        await PostAsync(api, Erika, $"/api/logbook/reports/{report}/escalate", new { note = "x" }, HttpStatusCode.Forbidden); // no review privilege
        await PostAsync(api, null, $"/api/logbook/reports/{report}/escalate", new { note = "x" }, HttpStatusCode.Unauthorized);
        await PostAsync(api, Nils, $"/api/logbook/reports/{report}/escalate", new { note = "x" }, HttpStatusCode.NotFound); // regional leader of another region
        await PostAsync(api, Ben, $"/api/logbook/reports/{report}/escalate", new { note = "x" }, HttpStatusCode.NotFound); // leads nothing
        await PostAsync(api, It, $"/api/logbook/reports/{Guid.NewGuid()}/escalate", new { note = "x" }, HttpStatusCode.NotFound);
        await PostAsync(api, It, $"/api/logbook/reports/{report}/escalate", new { note = new string('x', 1001) }, HttpStatusCode.UnprocessableEntity);

        await PostAsync(api, It, $"/api/logbook/reports/{report}/escalate", new { note = (string?)null }); // the note is optional
    }

    // ------------------------------------------------------------------ reminding by hand

    [Fact]
    public async Task A_leader_reminds_the_store_managers_of_a_store_without_a_report_and_the_reminder_is_generic()
    {
        await using var api = await StartUpAsync(postgres);

        var reminder = await PostAsync(api, It, "/api/logbook/reminders", new { storeId = StoreA });

        reminder.GetProperty("week").GetString().ShouldBe(ThisWeek);
        var sent = (await SentAsync(api, LogbookNotifier.Reminder)).ShouldHaveSingleItem();
        Recipients(sent).ShouldBe([Erika]);
        sent.Message.DeepLink.ShouldBe("/logbook");
        sent.Message.Data.ShouldBeEmpty();
        sent.Payload.ShouldNotContain("Store A");
    }

    [Fact]
    public async Task A_manual_reminder_is_refused_when_the_report_is_in_when_there_was_one_a_short_time_ago_or_nobody_is_store_manager()
    {
        await using var api = await StartUpAsync(postgres);
        var clock = api.Services.GetRequiredService<FakeTimeProvider>();

        await PostAsync(api, It, "/api/logbook/reminders", new { storeId = StoreA });
        await PostAsync(api, Mia, "/api/logbook/reminders", new { storeId = StoreA }, HttpStatusCode.Conflict); // 0 hours later
        clock.Advance(TimeSpan.FromHours(11));
        await PostAsync(api, It, "/api/logbook/reminders", new { storeId = StoreA }, HttpStatusCode.Conflict); // 11 hours: still too soon
        clock.Advance(TimeSpan.FromHours(2));
        await PostAsync(api, It, "/api/logbook/reminders", new { storeId = StoreA }); // 13 hours

        await SubmittedAsync(api, Fiona, StoreB);
        await PostAsync(api, It, "/api/logbook/reminders", new { storeId = StoreB }, HttpStatusCode.Conflict); // handed in

        api.Grant(Hanna, TestGrant.Everywhere(PortalPrivileges.LogbookManageStructure)); // nobody is store manager of C any more
        await PostAsync(api, Nils, "/api/logbook/reminders", new { storeId = StoreC }, HttpStatusCode.Conflict);
        (await SentAsync(api, LogbookNotifier.Reminder)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Only_a_leader_of_the_store_reminds_and_the_week_is_checked()
    {
        await using var api = await StartUpAsync(postgres);

        await PostAsync(api, Erika, "/api/logbook/reminders", new { storeId = StoreA }, HttpStatusCode.Forbidden); // store manager, not a reviewer
        await PostAsync(api, null, "/api/logbook/reminders", new { storeId = StoreA }, HttpStatusCode.Unauthorized);
        await PostAsync(api, Nils, "/api/logbook/reminders", new { storeId = StoreA }, HttpStatusCode.NotFound); // other region
        await PostAsync(api, Ben, "/api/logbook/reminders", new { storeId = StoreA }, HttpStatusCode.NotFound); // leads nothing
        await PostAsync(api, It, "/api/logbook/reminders", new { storeId = (Guid?)null }, HttpStatusCode.UnprocessableEntity);
        await PostAsync(api, It, "/api/logbook/reminders", new { storeId = StoreA, week = "2026-10-26" }, HttpStatusCode.UnprocessableEntity); // next week
        await PostAsync(api, It, "/api/logbook/reminders", new { storeId = StoreA, week = "2026-08-17" }, HttpStatusCode.UnprocessableEntity); // nine weeks back
        await PostAsync(api, Revisor, "/api/logbook/reminders", new { storeId = StoreC }); // the district leader reaches every store
        (await SentAsync(api, LogbookNotifier.Reminder)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_list_of_missing_reports_shows_what_is_missing_who_was_reminded_and_who_could_be()
    {
        await using var api = await StartUpAsync(postgres);
        await SubmittedAsync(api, Erika, StoreA);
        (await api.SendAsync(HttpMethod.Put, $"/api/logbook/reports/{StoreB}/{ThisWeek}", Fiona, new { answers = Complete() })).StatusCode.ShouldBe(HttpStatusCode.OK); // a draft only
        await PostAsync(api, It, "/api/logbook/reminders", new { storeId = StoreB });

        using var response = await api.SendAsync(HttpMethod.Get, "/api/logbook/missing", Revisor);
        var missing = await DataAsync(response);

        missing.GetProperty("week").GetString().ShouldBe(ThisWeek);
        var stores = missing.GetProperty("stores").EnumerateArray().ToList();
        stores.Select(s => s.GetProperty("storeId").GetGuid()).ShouldBe([StoreB, StoreC]); // A handed in, sorted by store number
        var b = stores[0];
        b.GetProperty("remindersSent").GetInt32().ShouldBe(1);
        b.GetProperty("lastReminderAt").GetDateTimeOffset().ShouldBe(Start);
        b.GetProperty("reportStarted").GetBoolean().ShouldBeTrue();
        b.GetProperty("hasStoreManagers").GetBoolean().ShouldBeTrue();
        stores[1].GetProperty("remindersSent").GetInt32().ShouldBe(0);
        stores[1].GetProperty("reportStarted").GetBoolean().ShouldBeFalse();

        using var regional = await api.SendAsync(HttpMethod.Get, "/api/logbook/missing", It);
        (await DataAsync(regional)).GetProperty("stores").EnumerateArray().Select(s => s.GetProperty("storeId").GetGuid()).ShouldBe([StoreB]); // only the own region
        (await api.SendAsync(HttpMethod.Get, "/api/logbook/missing", Erika)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ------------------------------------------------------------------ the settings

    [Fact]
    public async Task The_settings_start_with_the_default_and_are_changed_by_the_holders_of_the_privilege_only()
    {
        await using var api = await StartUpAsync(postgres);

        using var first = await api.SendAsync(HttpMethod.Get, "/api/logbook/reminder-settings", Hanna);
        var settings = await DataAsync(first);
        settings.GetProperty("enabled").GetBoolean().ShouldBeTrue();
        settings.GetProperty("dueDay").GetString().ShouldBe("friday");
        settings.GetProperty("dueTime").GetString().ShouldBe("18:00");
        settings.GetProperty("maxReminders").GetInt32().ShouldBe(2);
        settings.GetProperty("points").EnumerateArray().Select(p => (p.GetProperty("daysAfterDue").GetInt32(), p.GetProperty("time").GetString())).ShouldBe([(0, "12:00"), (3, "09:00")]);

        foreach (var person in new Guid?[] { Erika, It, Revisor, null })
        {
            var expected = person is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
            (await api.SendAsync(HttpMethod.Get, "/api/logbook/reminder-settings", person)).StatusCode.ShouldBe(expected);
            (await api.SendAsync(HttpMethod.Put, "/api/logbook/reminder-settings", person, Settings())).StatusCode.ShouldBe(expected);
        }
    }

    private static object Settings(bool enabled = true, string dueDay = "friday", string dueTime = "18:00", int max = 2, params (int Days, string Time)[] points) => new
    {
        enabled, dueDay, dueTime, maxReminders = max,
        points = (points.Length == 0 ? new (int Days, string Time)[] { (0, "12:00"), (3, "09:00") } : points).Select(p => new { daysAfterDue = p.Days, time = p.Time }).ToArray(),
    };

    [Fact]
    public async Task The_settings_are_saved_checked_and_switching_on_sets_the_time_while_other_changes_keep_it()
    {
        await using var api = await StartUpAsync(postgres);
        var clock = api.Services.GetRequiredService<FakeTimeProvider>();

        using (var off = await api.SendAsync(HttpMethod.Put, "/api/logbook/reminder-settings", Hanna, Settings(enabled: false, dueDay: "thursday", dueTime: "17:30", max: 3, points: [(1, "08:00"), (0, "16:00")])))
        {
            var saved = await DataAsync(off);
            saved.GetProperty("enabled").GetBoolean().ShouldBeFalse();
            saved.GetProperty("dueDay").GetString().ShouldBe("thursday");
            saved.GetProperty("dueTime").GetString().ShouldBe("17:30");
            saved.GetProperty("points").EnumerateArray().Select(p => p.GetProperty("time").GetString()).ShouldBe(["16:00", "08:00"]); // in time order
        }

        clock.Advance(TimeSpan.FromDays(1));
        using var on = await api.SendAsync(HttpMethod.Put, "/api/logbook/reminder-settings", Hanna, Settings());
        (await DataAsync(on)).GetProperty("enabledAt").GetDateTimeOffset().ShouldBe(Start.AddDays(1)); // switched on now

        clock.Advance(TimeSpan.FromDays(1));
        using var changed = await api.SendAsync(HttpMethod.Put, "/api/logbook/reminder-settings", Hanna, Settings(max: 5));
        (await DataAsync(changed)).GetProperty("enabledAt").GetDateTimeOffset().ShouldBe(Start.AddDays(1)); // still on: the time stays
    }

    [Fact]
    public async Task Wrong_settings_are_refused_with_the_field_and_nothing_changes()
    {
        await using var api = await StartUpAsync(postgres);
        var tooMany = Enumerable.Range(0, 11).Select(i => (i, "09:00")).ToArray();
        var cases = new (object Body, string Field)[]
        {
            (Settings(dueDay: "freitag"), "dueDay"),
            (Settings(dueTime: "25:00"), "dueTime"),
            (Settings(max: 0), "maxReminders"),
            (Settings(max: 11), "maxReminders"),
            (Settings(points: [(15, "09:00")]), "points"),
            (Settings(points: [(1, "09:00"), (1, "09:00")]), "points"),
            (Settings(points: [(1, "nine")]), "points"),
            (Settings(points: tooMany), "points"),
            (new { enabled = (bool?)null, dueDay = "friday", dueTime = "18:00", maxReminders = 2, points = Array.Empty<object>() }, "enabled"),
            (new { enabled = true, dueDay = "friday", dueTime = "18:00", maxReminders = 2, points = (object?)null }, "points"),
        };

        foreach (var (body, field) in cases)
        {
            using var response = await api.SendAsync(HttpMethod.Put, "/api/logbook/reminder-settings", Hanna, body);
            response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, field);
            (await ErrorAsync(response)).GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("field").GetString()).ShouldContain(field);
        }

        using var scope = api.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<PortalDbContext>().LogbookReminderSettings.CountAsync()).ShouldBe(0);
    }

    // ------------------------------------------------------------------ the automatic reminder

    private static LogbookReminderBackgroundService Reminders(PortalApi api) =>
        api.Services.GetServices<IHostedService>().OfType<LogbookReminderBackgroundService>().Single();

    private static async Task SetSettingsAsync(PortalApi api, object settings)
    {
        using var response = await api.SendAsync(HttpMethod.Put, "/api/logbook/reminder-settings", Hanna, settings);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task At_the_point_the_stores_without_a_handed_in_report_are_reminded_once_and_never_again()
    {
        await using var api = await StartUpAsync(postgres);
        await SetSettingsAsync(api, Settings()); // switched on now (Friday 14:00 local): the Friday point (12:00 local) is before
        await SubmittedAsync(api, Erika, StoreA); // A is in
        var clock = api.Services.GetRequiredService<FakeTimeProvider>();

        clock.SetUtcNow(new DateTimeOffset(2026, 10, 26, 7, 59, 0, TimeSpan.Zero)); // Monday 08:59 local time (CET): one minute before the point
        await Reminders(api).RunOnceAsync(CancellationToken.None);
        (await SentAsync(api, LogbookNotifier.Reminder)).ShouldBeEmpty();

        clock.SetUtcNow(new DateTimeOffset(2026, 10, 26, 8, 0, 0, TimeSpan.Zero)); // Monday 09:00 local time
        await Reminders(api).RunOnceAsync(CancellationToken.None);
        await Reminders(api).RunOnceAsync(CancellationToken.None); // a second round the same hour does not repeat it
        clock.Advance(TimeSpan.FromDays(1));
        await Reminders(api).RunOnceAsync(CancellationToken.None);

        var sent = await SentAsync(api, LogbookNotifier.Reminder);
        sent.SelectMany(Recipients).Order().ShouldBe([.. new[] { Fiona, Hanna }.Order()]); // B and C, not A
        sent.ShouldAllBe(n => n.Message.Data.Count == 0 && n.Message.DeepLink == "/logbook");
        sent.Select(n => n.Message.NotificationId).Distinct().Count().ShouldBe(2);
        using var scope = api.Services.CreateScope();
        var rows = await scope.ServiceProvider.GetRequiredService<PortalDbContext>().LogbookReminders.AsNoTracking().ToListAsync();
        rows.ShouldAllBe(r => r.Kind == LogbookReminderKind.Automatic && r.PointIndex == 1 && r.Week == new DateOnly(2026, 10, 19));
    }

    [Fact]
    public async Task Nothing_is_sent_while_switched_off_and_a_report_that_was_handed_in_late_stops_the_reminders()
    {
        await using var api = await StartUpAsync(postgres);
        await SetSettingsAsync(api, Settings(enabled: false));
        var clock = api.Services.GetRequiredService<FakeTimeProvider>();
        clock.SetUtcNow(new DateTimeOffset(2026, 10, 26, 8, 0, 0, TimeSpan.Zero));

        await Reminders(api).RunOnceAsync(CancellationToken.None);
        (await SentAsync(api, LogbookNotifier.Reminder)).ShouldBeEmpty();

        await SetSettingsAsync(api, Settings(points: [(0, "09:00"), (3, "09:00"), (4, "09:00")])); // on again now: Friday and Monday 09:00 are before
        (await api.SendAsync(HttpMethod.Put, $"/api/logbook/reports/{StoreB}/{ThisWeek}", Fiona, new { answers = Complete() })).StatusCode.ShouldBe(HttpStatusCode.OK);
        await PostAsync(api, Fiona, $"/api/logbook/reports/{StoreB}/{ThisWeek}/submit");
        clock.SetUtcNow(new DateTimeOffset(2026, 10, 27, 8, 0, 0, TimeSpan.Zero)); // Tuesday 09:00 local: the third point
        await Reminders(api).RunOnceAsync(CancellationToken.None);

        (await SentAsync(api, LogbookNotifier.Reminder)).SelectMany(Recipients).Order().ShouldBe([.. new[] { Erika, Hanna }.Order()]); // A and C; B handed in
    }

    [Fact]
    public async Task Manual_reminders_count_towards_the_maximum_of_the_week()
    {
        await using var api = await StartUpAsync(postgres);
        await SetSettingsAsync(api, Settings(max: 1));
        await SubmittedAsync(api, Erika, StoreA);
        await PostAsync(api, It, "/api/logbook/reminders", new { storeId = StoreB }); // one reminder for B already
        api.Services.GetRequiredService<FakeTimeProvider>().SetUtcNow(new DateTimeOffset(2026, 10, 26, 8, 0, 0, TimeSpan.Zero));

        await Reminders(api).RunOnceAsync(CancellationToken.None);

        (await SentAsync(api, LogbookNotifier.Reminder)).Count.ShouldBe(2); // the manual one to B and one automatic one
        using var scope = api.Services.CreateScope();
        var automatic = await scope.ServiceProvider.GetRequiredService<PortalDbContext>().LogbookReminders.AsNoTracking().Where(r => r.Kind == LogbookReminderKind.Automatic).ToListAsync();
        automatic.Select(r => r.StoreId).ShouldBe([StoreC]); // B reached its maximum (the manual reminder counts), only C is reminded
    }

    [Fact]
    public async Task The_first_round_without_settings_starts_the_reminder_now_and_does_not_remind_for_weeks_that_are_long_over()
    {
        await using var api = await StartUpAsync(postgres);
        api.Services.GetRequiredService<FakeTimeProvider>().SetUtcNow(new DateTimeOffset(2026, 10, 27, 8, 0, 0, TimeSpan.Zero)); // Tuesday: the points of last week are over

        await Reminders(api).RunOnceAsync(CancellationToken.None);

        (await SentAsync(api, LogbookNotifier.Reminder)).ShouldBeEmpty();
        using var response = await api.SendAsync(HttpMethod.Get, "/api/logbook/reminder-settings", Hanna);
        (await DataAsync(response)).GetProperty("enabledAt").GetDateTimeOffset().ShouldBe(new DateTimeOffset(2026, 10, 27, 8, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task A_store_without_any_store_manager_is_not_marked_so_a_later_round_still_reminds()
    {
        await using var api = await StartUpAsync(postgres);
        await SetSettingsAsync(api, Settings());
        api.Grant(Hanna, TestGrant.Everywhere(PortalPrivileges.LogbookManageStructure), TestGrant.Everywhere(PortalPrivileges.LogbookManageReminders)); // C has nobody
        var clock = api.Services.GetRequiredService<FakeTimeProvider>();
        clock.SetUtcNow(new DateTimeOffset(2026, 10, 26, 8, 0, 0, TimeSpan.Zero));

        await Reminders(api).RunOnceAsync(CancellationToken.None);
        using (var scope = api.Services.CreateScope())
        {
            (await scope.ServiceProvider.GetRequiredService<PortalDbContext>().LogbookReminders.AnyAsync(r => r.StoreId == StoreC)).ShouldBeFalse();
        }

        api.Grant(Hanna, TestGrant.Everywhere(PortalPrivileges.LogbookManageStructure), new TestGrant(PortalPrivileges.LogbookWrite, [StoreC])); // somebody is entered
        clock.Advance(TimeSpan.FromHours(1));
        await Reminders(api).RunOnceAsync(CancellationToken.None);

        (await SentAsync(api, LogbookNotifier.Reminder)).SelectMany(Recipients).ShouldContain(Hanna);
    }
}
