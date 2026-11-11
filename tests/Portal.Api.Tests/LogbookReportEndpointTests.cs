using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Portal.Core;
using Portal.Core.Logbook;
using Portal.Infrastructure;
using Shared.Testing;
using static Portal.Api.Tests.PortalApi;

namespace Portal.Api.Tests;

/// <summary>
/// The weekly report through the real API and database (decision 49): one report per store and week, written as a draft, handed in, and still changeable
/// afterwards (the regional leader then sees that it was revised). Written by everybody who holds the write privilege for the store. Clock: Friday
/// 2026-10-23, 12:00 UTC, so the current week is the one of Monday 2026-10-19.
/// </summary>
public sealed class LogbookReportEndpointTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string ThisWeek = "2026-10-19";

    private static readonly Guid DistrictSouth = Guid.Parse("00000000-0000-0000-0000-0000000000d1");
    private static readonly Guid RegionAugsburg = Guid.Parse("00000000-0000-0000-0000-0000000000a1");

    // Erika and Ben are store managers of store A (Ben also has the privilege for store D, which is not in the structure). Revisor is the district leader, It the regional leader.
    private static readonly Guid StoreManager = Erika;
    private static readonly Guid SecondStoreManager = Ben;

    private static object Structure() => new
    {
        districts = new object[] { new { id = DistrictSouth, name = "Süd", leaderIds = new[] { Revisor } } },
        regions = new object[] { new { id = RegionAugsburg, districtId = DistrictSouth, name = "Augsburg", leaderIds = new[] { It } } },
        stores = new object[]
        {
            new { storeId = StoreA, districtId = DistrictSouth, regionId = (Guid?)RegionAugsburg },
            new { storeId = StoreB, districtId = DistrictSouth, regionId = (Guid?)RegionAugsburg },
            new { storeId = StoreC, districtId = DistrictSouth, regionId = (Guid?)null },
        },
    };

    private static async Task<PortalApi> StartUpAsync(PostgresFixture postgres)
    {
        var api = await StartAsync(postgres);
        using var response = await api.SendAsync(HttpMethod.Put, "/api/logbook/structure", Hanna, Structure());
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        api.Grant(StoreManager, new TestGrant(PortalPrivileges.LogbookWrite, [StoreA]));
        api.Grant(SecondStoreManager, new TestGrant(PortalPrivileges.LogbookWrite, [StoreA, StoreD]));
        return api;
    }

    /// <summary>The default template: the ids of the answers.</summary>
    private static LogbookSection Section(int index) => LogbookTemplate.Default.Sections[index];

    private static Dictionary<string, object> Complete(string text = "Alles ruhig.") => new()
    {
        [Section(0).Id.ToString()] = new { optionIds = new[] { Section(0).Options[0].Id }, text = (string?)null }, // overall: runs well
        [Section(1).Id.ToString()] = new { optionIds = new[] { Section(1).Options[0].Id }, text = (string?)null }, // staff: full
        [Section(3).Id.ToString()] = new { optionIds = Array.Empty<Guid>(), text = (string?)text }, // what went well
        [Section(5).Id.ToString()] = new { optionIds = new[] { Section(5).Options[0].Id }, text = (string?)null }, // help: no
    };

    private static string Url(Guid store, string week = ThisWeek) => $"/api/logbook/reports/{store}/{week}";

    private static async Task<HttpResponseMessage> SaveAsync(PortalApi api, Guid person, Dictionary<string, object> answers, Guid? store = null, string week = ThisWeek) =>
        await api.SendAsync(HttpMethod.Put, Url(store ?? StoreA, week), person, new { answers });

    private static async Task<JsonElement> SavedAsync(PortalApi api, Guid person, Dictionary<string, object> answers, Guid? store = null, string week = ThisWeek)
    {
        using var response = await SaveAsync(api, person, answers, store, week);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await DataAsync(response);
    }

    private static async Task<HttpResponseMessage> SubmitAsync(PortalApi api, Guid person, Guid? store = null, string week = ThisWeek) =>
        await api.SendAsync(HttpMethod.Post, Url(store ?? StoreA, week) + "/submit", person);

    private static async Task<JsonElement> MineAsync(PortalApi api, Guid person, string? week = null)
    {
        using var response = await api.SendAsync(HttpMethod.Get, "/api/logbook/my-reports" + (week is null ? string.Empty : "?week=" + week), person);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await DataAsync(response);
    }

    private static JsonElement ItemOf(JsonElement mine, Guid store) => mine.GetProperty("stores").EnumerateArray().Single(s => s.GetProperty("storeId").GetGuid() == store);

    private static async Task<List<string?>> ErrorFieldsAsync(HttpResponseMessage response) =>
        [.. (await ErrorAsync(response)).GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("field").GetString())];

    // ------------------------------------------------------------------ what a store manager sees

    [Fact]
    public async Task The_store_manager_sees_the_stores_of_the_reach_for_the_current_week_without_a_report_yet()
    {
        await using var api = await StartUpAsync(postgres);

        var mine = await MineAsync(api, StoreManager);

        mine.GetProperty("week").GetString().ShouldBe(ThisWeek);
        var item = ItemOf(mine, StoreA);
        item.GetProperty("storeName").GetString().ShouldBe("Store A");
        item.GetProperty("storeNumber").GetString().ShouldBe("017");
        item.GetProperty("report").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("canSubmit").GetBoolean().ShouldBeTrue();
        item.GetProperty("templateVersion").GetInt32().ShouldBe(0); // the suggestion applies, nothing saved yet
        mine.GetProperty("stores").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task A_store_that_nobody_is_told_about_is_listed_but_cannot_be_handed_in()
    {
        await using var api = await StartUpAsync(postgres);

        var item = ItemOf(await MineAsync(api, SecondStoreManager), StoreD); // D is not in the structure

        item.GetProperty("canSubmit").GetBoolean().ShouldBeFalse();
        (await SubmitAsync(api, SecondStoreManager, StoreD)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    // ------------------------------------------------------------------ writing a draft

    [Fact]
    public async Task A_draft_is_saved_and_changed_and_stays_one_report()
    {
        await using var api = await StartUpAsync(postgres);

        var first = await SavedAsync(api, StoreManager, new() { [Section(3).Id.ToString()] = new { optionIds = Array.Empty<Guid>(), text = (string?)"Erster Stand" } });
        var second = await SavedAsync(api, StoreManager, Complete("Zweiter Stand"));

        first.GetProperty("phase").GetString().ShouldBe("draft");
        second.GetProperty("id").GetGuid().ShouldBe(first.GetProperty("id").GetGuid());
        second.GetProperty("answers").GetProperty(Section(3).Id.ToString()).GetProperty("text").GetString().ShouldBe("Zweiter Stand");
        second.GetProperty("submittedAt").ValueKind.ShouldBe(JsonValueKind.Null);
        using var scope = api.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<PortalDbContext>().LogbookReports.CountAsync()).ShouldBe(1);
        (await MineAsync(api, StoreManager)).GetProperty("stores")[0].GetProperty("report").GetProperty("id").GetGuid().ShouldBe(first.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task A_draft_may_be_incomplete_but_wrong_answers_are_refused_with_the_field()
    {
        await using var api = await StartUpAsync(postgres);

        (await SaveAsync(api, StoreManager, [])).StatusCode.ShouldBe(HttpStatusCode.OK); // nothing yet is fine for a draft

        var unknownSection = new Dictionary<string, object> { [Guid.NewGuid().ToString()] = new { optionIds = Array.Empty<Guid>(), text = (string?)"x" } };
        var tooMany = new Dictionary<string, object> { [Section(0).Id.ToString()] = new { optionIds = new[] { Section(0).Options[0].Id, Section(0).Options[1].Id }, text = (string?)null } };
        var textInChoice = new Dictionary<string, object> { [Section(0).Id.ToString()] = new { optionIds = new[] { Section(0).Options[0].Id }, text = (string?)"Anmerkung" } };
        var tooLong = new Dictionary<string, object> { [Section(3).Id.ToString()] = new { optionIds = Array.Empty<Guid>(), text = (string?)new string('x', LogbookLimits.MaxText + 1) } };

        foreach (var answers in new[] { unknownSection, tooMany, textInChoice, tooLong })
        {
            using var response = await SaveAsync(api, StoreManager, answers);
            response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
            (await ErrorFieldsAsync(response)).ShouldContain("answers");
        }

        using var noBody = await api.SendAsync(HttpMethod.Put, Url(StoreA), StoreManager, new { answers = (object?)null });
        noBody.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Everybody_with_the_privilege_for_the_store_writes_on_the_same_report()
    {
        await using var api = await StartUpAsync(postgres);

        var byFirst = await SavedAsync(api, StoreManager, Complete("Von Erika"));
        var bySecond = await SavedAsync(api, SecondStoreManager, Complete("Von Ben"));

        bySecond.GetProperty("id").GetGuid().ShouldBe(byFirst.GetProperty("id").GetGuid());
        (await MineAsync(api, StoreManager)).GetProperty("stores")[0].GetProperty("report").GetProperty("answers").GetProperty(Section(3).Id.ToString()).GetProperty("text").GetString().ShouldBe("Von Ben");
    }

    // ------------------------------------------------------------------ handing in

    [Fact]
    public async Task Handing_in_needs_the_required_sections_and_then_the_report_is_submitted_by_the_person_at_the_time()
    {
        await using var api = await StartUpAsync(postgres);
        await SavedAsync(api, StoreManager, new() { [Section(3).Id.ToString()] = new { optionIds = Array.Empty<Guid>(), text = (string?)"Nur Text" } });

        using var incomplete = await SubmitAsync(api, StoreManager);
        incomplete.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ErrorFieldsAsync(incomplete)).ShouldContain("answers");

        await SavedAsync(api, StoreManager, Complete());
        using var response = await SubmitAsync(api, StoreManager);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var report = await DataAsync(response);

        report.GetProperty("phase").GetString().ShouldBe("submitted");
        report.GetProperty("state").GetString().ShouldBe("new");
        report.GetProperty("submittedAt").GetDateTimeOffset().ShouldBe(Start);
        report.GetProperty("submittedByName").GetString().ShouldBe("Erika Mustermann");
        report.GetProperty("revisedAt").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Handing_in_without_any_report_yet_is_refused_and_a_second_hand_in_changes_nothing()
    {
        await using var api = await StartUpAsync(postgres);

        (await SubmitAsync(api, StoreManager)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity); // required sections are missing

        await SavedAsync(api, StoreManager, Complete());
        using var first = await SubmitAsync(api, StoreManager);
        var firstReport = await DataAsync(first);
        api.Services.GetRequiredService<FakeTimeProvider>().Advance(TimeSpan.FromHours(3));
        using var second = await SubmitAsync(api, StoreManager);

        second.StatusCode.ShouldBe(HttpStatusCode.OK); // a double click is no error
        (await DataAsync(second)).GetProperty("submittedAt").GetDateTimeOffset().ShouldBe(firstReport.GetProperty("submittedAt").GetDateTimeOffset());
    }

    // ------------------------------------------------------------------ revising after the hand in

    [Fact]
    public async Task A_handed_in_report_can_be_revised_and_the_revision_is_marked_with_who_and_when()
    {
        await using var api = await StartUpAsync(postgres);
        await SavedAsync(api, StoreManager, Complete());
        await SubmitAsync(api, StoreManager);
        api.Services.GetRequiredService<FakeTimeProvider>().Advance(TimeSpan.FromDays(1));

        var revised = await SavedAsync(api, SecondStoreManager, Complete("Nachträglich ergänzt"));

        revised.GetProperty("phase").GetString().ShouldBe("submitted"); // stays handed in
        revised.GetProperty("answers").GetProperty(Section(3).Id.ToString()).GetProperty("text").GetString().ShouldBe("Nachträglich ergänzt");
        revised.GetProperty("revisedAt").GetDateTimeOffset().ShouldBe(Start.AddDays(1));
        revised.GetProperty("revisedByName").GetString().ShouldBe("Ben Beispiel");
        revised.GetProperty("submittedAt").GetDateTimeOffset().ShouldBe(Start); // the hand in stays as it was
    }

    [Fact]
    public async Task A_change_that_leaves_the_answers_as_they_are_is_no_revision()
    {
        await using var api = await StartUpAsync(postgres);
        await SavedAsync(api, StoreManager, Complete());
        await SubmitAsync(api, StoreManager);
        api.Services.GetRequiredService<FakeTimeProvider>().Advance(TimeSpan.FromDays(1));

        var same = await SavedAsync(api, StoreManager, Complete());

        same.GetProperty("revisedAt").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_revision_of_a_handed_in_report_must_keep_the_required_sections_answered()
    {
        await using var api = await StartUpAsync(postgres);
        await SavedAsync(api, StoreManager, Complete());
        await SubmitAsync(api, StoreManager);

        using var response = await SaveAsync(api, StoreManager, new() { [Section(3).Id.ToString()] = new { optionIds = Array.Empty<Guid>(), text = (string?)"Nur noch Text" } });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ErrorFieldsAsync(response)).ShouldContain("answers");
        (await MineAsync(api, StoreManager)).GetProperty("stores")[0].GetProperty("report").GetProperty("answers").GetProperty(Section(3).Id.ToString()).GetProperty("text").GetString().ShouldBe("Alles ruhig.");
    }

    // ------------------------------------------------------------------ weeks

    [Fact]
    public async Task A_day_in_the_middle_of_the_week_means_the_week_and_the_future_and_the_distant_past_are_refused()
    {
        await using var api = await StartUpAsync(postgres);

        var midweek = await SavedAsync(api, StoreManager, Complete(), week: "2026-10-21"); // Wednesday
        midweek.GetProperty("week").GetString().ShouldBe(ThisWeek);

        (await SaveAsync(api, StoreManager, Complete(), week: "2026-10-26")).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity); // next week
        (await SaveAsync(api, StoreManager, Complete(), week: "2026-08-24")).StatusCode.ShouldBe(HttpStatusCode.OK); // exactly eight weeks back
        (await SaveAsync(api, StoreManager, Complete(), week: "2026-08-17")).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity); // nine weeks back
        (await SubmitAsync(api, StoreManager, week: "2026-10-26")).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await api.SendAsync(HttpMethod.Put, "/api/logbook/reports/" + StoreA + "/kein-datum", StoreManager, new { answers = new { } })).StatusCode.ShouldBeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.NotFound, HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task The_week_changes_at_local_midnight_on_the_new_week()
    {
        await using var api = await StartUpAsync(postgres);
        var clock = api.Services.GetRequiredService<FakeTimeProvider>();

        clock.SetUtcNow(new DateTimeOffset(2026, 11, 1, 22, 30, 0, TimeSpan.Zero)); // Sunday 23:30 local time
        (await MineAsync(api, StoreManager)).GetProperty("week").GetString().ShouldBe("2026-10-26");

        clock.SetUtcNow(new DateTimeOffset(2026, 11, 1, 23, 30, 0, TimeSpan.Zero)); // Monday 00:30 local time
        (await MineAsync(api, StoreManager)).GetProperty("week").GetString().ShouldBe("2026-11-02");
    }

    [Fact]
    public async Task A_report_keeps_the_template_version_it_was_started_with_and_the_next_week_gets_the_current_one()
    {
        await using var api = await StartUpAsync(postgres);
        await SavedAsync(api, StoreManager, Complete()); // version 0 (the suggestion)

        var changed = new
        {
            sections = new object[] { new { id = (Guid?)null, title = "Neu", hint = (string?)null, kind = "freeText", required = false, options = Array.Empty<object>() } },
        };
        api.Grant(Hanna, TestGrant.Everywhere(PortalPrivileges.LogbookManageTemplate));
        (await api.SendAsync(HttpMethod.Put, "/api/logbook/template", Hanna, changed)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var item = ItemOf(await MineAsync(api, StoreManager), StoreA);
        item.GetProperty("report").GetProperty("templateVersion").GetInt32().ShouldBe(0); // the report stays on the version it was filled in with
        (await SavedAsync(api, StoreManager, Complete("Noch ein Stand"))).GetProperty("templateVersion").GetInt32().ShouldBe(0);

        var nextWeek = ItemOf(await MineAsync(api, StoreManager, "2026-10-12"), StoreA);
        nextWeek.GetProperty("templateVersion").GetInt32().ShouldBe(1); // no report yet that week: the current version
    }

    [Fact]
    public async Task The_history_lists_the_weeks_of_a_store_newest_first_with_phase_and_state()
    {
        await using var api = await StartUpAsync(postgres);
        await SavedAsync(api, StoreManager, Complete(), week: "2026-10-05");
        await SavedAsync(api, StoreManager, Complete());
        await SubmitAsync(api, StoreManager);

        using var response = await api.SendAsync(HttpMethod.Get, $"/api/logbook/stores/{StoreA}/reports", StoreManager);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var weeks = (await DataAsync(response)).EnumerateArray().ToList();

        weeks.Select(w => w.GetProperty("week").GetString()).ShouldBe([ThisWeek, "2026-10-05"]);
        weeks[0].GetProperty("phase").GetString().ShouldBe("submitted");
        weeks[0].GetProperty("state").GetString().ShouldBe("new");
        weeks[1].GetProperty("phase").GetString().ShouldBe("draft");
        (await api.SendAsync(HttpMethod.Get, $"/api/logbook/stores/{StoreB}/reports", StoreManager)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ------------------------------------------------------------------ who may

    [Fact]
    public async Task Only_those_with_the_write_privilege_for_the_store_may_write_read_and_hand_in()
    {
        await using var api = await StartUpAsync(postgres);
        api.Grant(It, TestGrant.Everywhere(PortalPrivileges.LogbookReview)); // reviewing is not writing

        foreach (var person in new Guid?[] { It, Revisor, null })
        {
            var expected = person is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
            using var save = await api.SendAsync(HttpMethod.Put, Url(StoreA), person, new { answers = new { } });
            save.StatusCode.ShouldBe(expected);
            (await api.SendAsync(HttpMethod.Post, Url(StoreA) + "/submit", person)).StatusCode.ShouldBe(expected);
            (await api.SendAsync(HttpMethod.Get, "/api/logbook/my-reports", person)).StatusCode.ShouldBe(expected);
        }
    }

    [Fact]
    public async Task The_privilege_counts_per_store_and_an_unknown_store_is_not_found()
    {
        await using var api = await StartUpAsync(postgres);

        (await SaveAsync(api, StoreManager, Complete(), store: StoreB)).StatusCode.ShouldBe(HttpStatusCode.Forbidden); // Erika reaches store A only
        (await SubmitAsync(api, StoreManager, StoreB)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SaveAsync(api, Hanna, Complete(), store: Guid.NewGuid())).StatusCode.ShouldBe(HttpStatusCode.NotFound); // Hanna reaches everything, but this store does not exist
        api.Grant(Hanna, TestGrant.Everywhere(PortalPrivileges.LogbookWrite));
        (await SaveAsync(api, Hanna, Complete(), store: StoreB)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Two_people_starting_the_same_report_at_the_same_time_end_with_one_report()
    {
        await using var api = await StartUpAsync(postgres);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(async i =>
        {
            using var response = await SaveAsync(api, i % 2 == 0 ? StoreManager : SecondStoreManager, Complete($"Stand {i}"));
            return response.StatusCode;
        }));

        results.ShouldAllBe(s => s == HttpStatusCode.OK || s == HttpStatusCode.Conflict);
        results.ShouldContain(HttpStatusCode.OK);
        using var scope = api.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<PortalDbContext>().LogbookReports.CountAsync()).ShouldBe(1);
    }
}
