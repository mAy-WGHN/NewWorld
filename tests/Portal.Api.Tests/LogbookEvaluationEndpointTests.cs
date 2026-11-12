using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Portal.Core;
using Portal.Core.Logbook;
using Portal.Infrastructure;
using Portal.Infrastructure.Logbook;
using Portal.Infrastructure.Messaging;
using Shared.Infrastructure.Audit;
using Shared.Testing;
using static Portal.Api.Tests.PortalApi;

namespace Portal.Api.Tests;

/// <summary>
/// What belongs to the rating and the year of the Berichtsheft (decision 49): the rating 1 to 5 is required to hand in, a report is on time until the due
/// moment of its week, the evaluation per store, region and district (who sees what), and the retention of one year with the figures that stay.
/// Clock: Friday 2026-10-23, 12:00 UTC (14:00 local); the report of the week 2026-10-19 is due on 2026-10-23 at 18:00 local time (16:00 UTC).
/// </summary>
public sealed class LogbookEvaluationEndpointTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
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

        api.Grant(Hanna, TestGrant.Everywhere(PortalPrivileges.LogbookManageStructure), new TestGrant(PortalPrivileges.LogbookWrite, [StoreC]));
        return api;
    }

    private static LogbookSection Section(int index) => LogbookTemplate.Default.Sections[index];

    /// <summary>All required sections of the suggestion; <paramref name="rating"/> 1 to 5, or none to leave the rating out.</summary>
    private static Dictionary<string, object> Complete(int? rating = 4)
    {
        var answers = new Dictionary<string, object>
        {
            [Section(1).Id.ToString()] = new { optionIds = new[] { Section(1).Options[0].Id }, text = (string?)null },
            [Section(3).Id.ToString()] = new { optionIds = Array.Empty<Guid>(), text = (string?)SecretText },
            [Section(5).Id.ToString()] = new { optionIds = new[] { Section(5).Options[0].Id }, text = (string?)null },
        };
        if (rating is { } value)
        {
            answers[LogbookRating.SectionId.ToString()] = new { optionIds = new[] { LogbookRating.OptionIds[value - 1] }, text = (string?)null };
        }

        return answers;
    }

    private static async Task<HttpResponseMessage> SaveAsync(PortalApi api, Guid manager, Guid store, Dictionary<string, object> answers) =>
        await api.SendAsync(HttpMethod.Put, $"/api/logbook/reports/{store}/{ThisWeek}", manager, new { answers });

    private static async Task<HttpResponseMessage> SubmitAsync(PortalApi api, Guid manager, Guid store) =>
        await api.SendAsync(HttpMethod.Post, $"/api/logbook/reports/{store}/{ThisWeek}/submit", manager);

    private static async Task<Guid> HandInAsync(PortalApi api, Guid manager, Guid store, int? rating = 4)
    {
        (await SaveAsync(api, manager, store, Complete(rating))).StatusCode.ShouldBe(HttpStatusCode.OK);
        using var response = await SubmitAsync(api, manager, store);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await DataAsync(response)).GetProperty("id").GetGuid();
    }

    private static FakeTimeProvider Clock(PortalApi api) => api.Services.GetRequiredService<FakeTimeProvider>();

    private static async Task<T> InDbAsync<T>(PortalApi api, Func<PortalDbContext, Task<T>> read)
    {
        using var scope = api.Services.CreateScope();
        return await read(scope.ServiceProvider.GetRequiredService<PortalDbContext>());
    }

    private static async Task<JsonElement> EvaluationAsync(PortalApi api, Guid? person, string query = "")
    {
        using var response = await api.SendAsync(HttpMethod.Get, "/api/logbook/evaluation" + query, person);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await DataAsync(response);
    }

    /// <summary>One round of the retention, under the lock of the background service: the service ticks when the test clock moves, two rounds at the same time would be one too many.</summary>
    private static async Task RunRetentionAsync(PortalApi api)
    {
        await using var held = await api.Services.GetRequiredService<Shared.Infrastructure.Persistence.IDistributedLock>().TryAcquireAsync("portal.logbook-reminders", CancellationToken.None);
        using var scope = api.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<LogbookRetentionService>().RunAsync(CancellationToken.None);
    }

    private static Guid[] StoreIds(JsonElement evaluation) => [.. evaluation.GetProperty("stores").EnumerateArray().Select(s => s.GetProperty("storeId").GetGuid())];

    private static JsonElement FiguresOf(JsonElement evaluation, Guid store) => evaluation.GetProperty("stores").EnumerateArray().Single(s => s.GetProperty("storeId").GetGuid() == store).GetProperty("figures");

    private static int[] Distribution(JsonElement figures) => [.. figures.GetProperty("distribution").EnumerateArray().Select(d => d.GetInt32())];

    // ------------------------------------------------------------------ the rating is required

    [Fact]
    public async Task A_report_without_the_rating_can_be_saved_as_a_draft_but_not_handed_in()
    {
        await using var api = await StartUpAsync(postgres);
        (await SaveAsync(api, Erika, StoreA, Complete(rating: null))).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var refused = await SubmitAsync(api, Erika, StoreA);

        refused.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ErrorAsync(refused)).GetProperty("errors").GetArrayLength().ShouldBeGreaterThan(0);
        (await api.OutboxAsync()).ShouldBeEmpty(); // nobody is told about a report that was not handed in

        (await SaveAsync(api, Erika, StoreA, Complete(rating: 3))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await SubmitAsync(api, Erika, StoreA)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_rating_is_one_of_the_five_values_and_nothing_else_is_taken()
    {
        await using var api = await StartUpAsync(postgres);
        var two = Complete(rating: null);
        two[LogbookRating.SectionId.ToString()] = new { optionIds = new[] { LogbookRating.OptionIds[0], LogbookRating.OptionIds[4] }, text = (string?)null };
        var foreign = Complete(rating: null);
        foreign[LogbookRating.SectionId.ToString()] = new { optionIds = new[] { Section(1).Options[0].Id }, text = (string?)null };

        (await SaveAsync(api, Erika, StoreA, two)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity); // one value, never two
        (await SaveAsync(api, Erika, StoreA, foreign)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity); // an option of another section
    }

    // ------------------------------------------------------------------ on time

    [Fact]
    public async Task A_report_is_on_time_until_the_due_moment_and_late_after_it_decided_when_it_is_handed_in()
    {
        await using var api = await StartUpAsync(postgres);
        await HandInAsync(api, Erika, StoreA); // Friday 14:00 local time: before 18:00
        Clock(api).SetUtcNow(new DateTimeOffset(2026, 10, 23, 16, 0, 0, TimeSpan.Zero)); // exactly the due moment
        await HandInAsync(api, Fiona, StoreB);
        Clock(api).SetUtcNow(new DateTimeOffset(2026, 10, 23, 16, 0, 1, TimeSpan.Zero)); // one second later
        await HandInAsync(api, Hanna, StoreC);

        var onTime = await InDbAsync(api, db => db.LogbookReports.AsNoTracking().ToDictionaryAsync(r => r.StoreId, r => r.SubmittedOnTime));

        onTime[StoreA].ShouldBe(true);
        onTime[StoreB].ShouldBe(true);
        onTime[StoreC].ShouldBe(false);
    }

    [Fact]
    public async Task Whether_a_report_was_on_time_does_not_change_when_the_due_day_is_changed_later()
    {
        await using var api = await StartUpAsync(postgres);
        api.Grant(Hanna, TestGrant.Everywhere(PortalPrivileges.LogbookManageStructure), TestGrant.Everywhere(PortalPrivileges.LogbookManageReminders), new TestGrant(PortalPrivileges.LogbookWrite, [StoreC]));
        Clock(api).SetUtcNow(new DateTimeOffset(2026, 10, 24, 9, 0, 0, TimeSpan.Zero)); // Saturday: late for a Friday deadline
        await HandInAsync(api, Erika, StoreA);

        using (var response = await api.SendAsync(HttpMethod.Put, "/api/logbook/reminder-settings", Hanna, new { enabled = true, dueDay = "sunday", dueTime = "23:00", maxReminders = 2, points = Array.Empty<object>() }))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        }

        (await InDbAsync(api, db => db.LogbookReports.AsNoTracking().SingleAsync(r => r.StoreId == StoreA))).SubmittedOnTime.ShouldBe(false);
        var year = await EvaluationAsync(api, Erika);
        FiguresOf(year, StoreA).GetProperty("onTime").GetInt32().ShouldBe(0); // still late, as it was when it was handed in
    }

    // ------------------------------------------------------------------ the evaluation of a year

    private static async Task<(Guid A, Guid B)> ScenarioAsync(PortalApi api)
    {
        var a = await HandInAsync(api, Erika, StoreA, rating: 5); // 14:00 local: on time
        using (var reply = await api.SendAsync(HttpMethod.Post, $"/api/logbook/reports/{a}/comments", It, new { kind = "reply", text = "Danke" }))
        {
            reply.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using (var done = await api.SendAsync(HttpMethod.Put, $"/api/logbook/reports/{a}/state", It, new { state = "done" }))
        {
            done.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        Clock(api).SetUtcNow(new DateTimeOffset(2026, 10, 23, 17, 0, 0, TimeSpan.Zero)); // 19:00 local: the week is overdue
        var b = await HandInAsync(api, Fiona, StoreB, rating: 3); // late
        using (var escalate = await api.SendAsync(HttpMethod.Post, $"/api/logbook/reports/{b}/escalate", It, new { note = (string?)null }))
        {
            escalate.StatusCode.ShouldBe(HttpStatusCode.OK, await escalate.Content.ReadAsStringAsync());
        }

        return (a, b); // the store C has not handed in anything
    }

    [Fact]
    public async Task The_district_leader_sees_every_store_the_regions_and_the_district_with_the_figures_added_up()
    {
        await using var api = await StartUpAsync(postgres);
        await ScenarioAsync(api);

        var evaluation = await EvaluationAsync(api, Revisor);

        evaluation.GetProperty("year").GetInt32().ShouldBe(2026);
        evaluation.GetProperty("asOf").GetDateTimeOffset().ShouldBe(new DateTimeOffset(2026, 10, 23, 17, 0, 0, TimeSpan.Zero));
        StoreIds(evaluation).ShouldBe([StoreA, StoreB, StoreC], ignoreOrder: true);

        // 2026 has 42 weeks that are due by now (the first Monday is 01-05, the last due one 10-19); A and B handed in one of them, C none.
        var a = FiguresOf(evaluation, StoreA);
        a.GetProperty("submitted").GetInt32().ShouldBe(1);
        a.GetProperty("onTime").GetInt32().ShouldBe(1);
        a.GetProperty("late").GetInt32().ShouldBe(0);
        a.GetProperty("missing").GetInt32().ShouldBe(41);
        a.GetProperty("expected").GetInt32().ShouldBe(42);
        a.GetProperty("ratingAverage").GetDouble().ShouldBe(5.0);
        a.GetProperty("answered").GetInt32().ShouldBe(1);
        a.GetProperty("done").GetInt32().ShouldBe(1);
        Distribution(a).ShouldBe([0, 0, 0, 0, 1]);

        var b = FiguresOf(evaluation, StoreB);
        b.GetProperty("onTime").GetInt32().ShouldBe(0);
        b.GetProperty("late").GetInt32().ShouldBe(1);
        b.GetProperty("ratingAverage").GetDouble().ShouldBe(3.0);
        b.GetProperty("escalated").GetInt32().ShouldBe(1);

        var c = FiguresOf(evaluation, StoreC);
        c.GetProperty("submitted").GetInt32().ShouldBe(0);
        c.GetProperty("missing").GetInt32().ShouldBe(42);
        c.GetProperty("ratingAverage").ValueKind.ShouldBe(JsonValueKind.Null); // nothing to average

        var regions = evaluation.GetProperty("regions").EnumerateArray().ToDictionary(r => r.GetProperty("name").GetString()!);
        regions.Keys.ShouldBe(["Augsburg", "München"], ignoreOrder: true);
        var augsburg = regions["Augsburg"];
        augsburg.GetProperty("stores").GetInt32().ShouldBe(2);
        var augsburgFigures = augsburg.GetProperty("figures");
        augsburgFigures.GetProperty("submitted").GetInt32().ShouldBe(2);
        augsburgFigures.GetProperty("onTime").GetInt32().ShouldBe(1);
        augsburgFigures.GetProperty("expected").GetInt32().ShouldBe(84);
        augsburgFigures.GetProperty("onTimeRate").GetDouble().ShouldBe(1.0 / 84, 0.0001);
        augsburgFigures.GetProperty("ratingAverage").GetDouble().ShouldBe(4.0); // (5 + 3) / 2
        Distribution(augsburgFigures).ShouldBe([0, 0, 1, 0, 1]);
        augsburgFigures.GetProperty("escalated").GetInt32().ShouldBe(1);
        augsburgFigures.GetProperty("answered").GetInt32().ShouldBe(1);
        augsburgFigures.GetProperty("done").GetInt32().ShouldBe(1);
        regions["München"].GetProperty("figures").GetProperty("missing").GetInt32().ShouldBe(42);

        var district = evaluation.GetProperty("districts").EnumerateArray().Single();
        district.GetProperty("name").GetString().ShouldBe("Süd");
        district.GetProperty("stores").GetInt32().ShouldBe(3);
        district.GetProperty("figures").GetProperty("submitted").GetInt32().ShouldBe(2);
        district.GetProperty("figures").GetProperty("expected").GetInt32().ShouldBe(126);
        evaluation.GetProperty("total").GetProperty("expected").GetInt32().ShouldBe(126);
    }

    [Fact]
    public async Task A_regional_leader_sees_the_own_region_a_store_manager_only_the_own_store_and_nobody_sees_more_than_the_reach()
    {
        await using var api = await StartUpAsync(postgres);
        await ScenarioAsync(api);

        var regional = await EvaluationAsync(api, It);
        StoreIds(regional).ShouldBe([StoreA, StoreB], ignoreOrder: true);
        regional.GetProperty("regions").EnumerateArray().Select(r => r.GetProperty("name").GetString()).ShouldBe(["Augsburg"]);
        regional.GetProperty("districts").GetArrayLength().ShouldBe(0); // the district is not theirs
        regional.GetProperty("total").GetProperty("submitted").GetInt32().ShouldBe(2);

        var munich = await EvaluationAsync(api, Nils);
        StoreIds(munich).ShouldBe([StoreC]);
        munich.GetProperty("regions").EnumerateArray().Select(r => r.GetProperty("name").GetString()).ShouldBe(["München"]);

        var storeManager = await EvaluationAsync(api, Erika);
        StoreIds(storeManager).ShouldBe([StoreA]);
        storeManager.GetProperty("regions").GetArrayLength().ShouldBe(0);
        storeManager.GetProperty("districts").GetArrayLength().ShouldBe(0);
        storeManager.GetProperty("total").GetProperty("expected").GetInt32().ShouldBe(42);

        StoreIds(await EvaluationAsync(api, Hanna)).ShouldBe([StoreC]); // store manager of C only, although they maintain the structure
    }

    [Fact]
    public async Task Somebody_who_leads_a_region_and_manages_a_store_elsewhere_sees_both_but_no_region_figures_for_the_other_store()
    {
        await using var api = await StartUpAsync(postgres);
        await ScenarioAsync(api);
        api.Grant(It, TestGrant.Everywhere(PortalPrivileges.LogbookReview), new TestGrant(PortalPrivileges.LogbookWrite, [StoreC]));

        var evaluation = await EvaluationAsync(api, It);

        StoreIds(evaluation).ShouldBe([StoreA, StoreB, StoreC], ignoreOrder: true);
        evaluation.GetProperty("regions").EnumerateArray().Select(r => r.GetProperty("name").GetString()).ShouldBe(["Augsburg"]); // not München: they do not lead it
        evaluation.GetProperty("total").GetProperty("expected").GetInt32().ShouldBe(126);
    }

    [Fact]
    public async Task Somebody_who_reaches_no_store_gets_nothing_and_the_year_is_checked()
    {
        await using var api = await StartUpAsync(postgres);

        (await api.SendAsync(HttpMethod.Get, "/api/logbook/evaluation", Ben)).StatusCode.ShouldBe(HttpStatusCode.Forbidden); // reviews, but leads nothing
        (await api.SendAsync(HttpMethod.Get, "/api/logbook/evaluation", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await api.SendAsync(HttpMethod.Get, "/api/logbook/evaluation?year=2019", Revisor)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await api.SendAsync(HttpMethod.Get, "/api/logbook/evaluation?year=2027", Revisor)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity); // not yet
        (await api.SendAsync(HttpMethod.Get, "/api/logbook/evaluation?year=2026", Revisor)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_year_that_is_over_counts_all_its_weeks_and_a_week_belongs_to_the_year_of_its_monday()
    {
        await using var api = await StartUpAsync(postgres);

        var last = await EvaluationAsync(api, Revisor, "?year=2025");

        FiguresOf(last, StoreA).GetProperty("missing").GetInt32().ShouldBe(52); // Mondays 2025-01-06 to 2025-12-29, the last one due on 2026-01-02
        FiguresOf(last, StoreA).GetProperty("submitted").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task Something_not_due_yet_is_not_counted_as_missing_and_a_report_handed_in_early_counts_as_handed_in()
    {
        await using var api = await StartUpAsync(postgres);
        await HandInAsync(api, Erika, StoreA, rating: 2); // the current week, due at 16:00 UTC: not due yet

        var evaluation = await EvaluationAsync(api, Revisor);

        var a = FiguresOf(evaluation, StoreA);
        a.GetProperty("submitted").GetInt32().ShouldBe(1);
        a.GetProperty("missing").GetInt32().ShouldBe(41); // the current week is not missing (yet), the 41 before it are
        a.GetProperty("expected").GetInt32().ShouldBe(42);
        FiguresOf(evaluation, StoreC).GetProperty("missing").GetInt32().ShouldBe(41);
    }

    // ------------------------------------------------------------------ one year of retention

    [Fact]
    public async Task A_report_is_kept_for_a_year_then_deleted_with_its_comments_and_reminders_but_the_figures_stay()
    {
        await using var api = await StartUpAsync(postgres);
        var a = await HandInAsync(api, Erika, StoreA, rating: 5);
        using (var reply = await api.SendAsync(HttpMethod.Post, $"/api/logbook/reports/{a}/comments", It, new { kind = "reply", text = SecretText }))
        {
            reply.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await SaveAsync(api, Fiona, StoreB, Complete(2))).StatusCode.ShouldBe(HttpStatusCode.OK); // a draft that is never handed in
        using (var remind = await api.SendAsync(HttpMethod.Post, "/api/logbook/reminders", Nils, new { storeId = StoreC, week = ThisWeek }))
        {
            remind.StatusCode.ShouldBe(HttpStatusCode.OK, await remind.Content.ReadAsStringAsync());
        }

        // A year after the Monday of the week: still there (the week begins on 2026-10-19, the year ends with 2027-10-19).
        Clock(api).SetUtcNow(new DateTimeOffset(2027, 10, 19, 12, 0, 0, TimeSpan.Zero));
        await RunRetentionAsync(api);
        (await InDbAsync(api, db => db.LogbookReports.CountAsync())).ShouldBe(2);

        // The day after it goes: the report and the draft, the comments with them, the reminder of that week.
        Clock(api).SetUtcNow(new DateTimeOffset(2027, 10, 20, 12, 0, 0, TimeSpan.Zero));
        await RunRetentionAsync(api);
        await RunRetentionAsync(api); // a second round has nothing left to do and changes nothing
        (await InDbAsync(api, db => db.LogbookReports.CountAsync())).ShouldBe(0);
        (await InDbAsync(api, db => db.LogbookComments.CountAsync())).ShouldBe(0);
        (await InDbAsync(api, db => db.LogbookReminders.CountAsync())).ShouldBe(0);

        // Of the handed-in report only numbers stay; of the draft nothing.
        var kept = await InDbAsync(api, db => db.LogbookWeekStats.AsNoTracking().ToListAsync());
        var row = kept.ShouldHaveSingleItem();
        (row.StoreId, row.Week, row.OnTime, row.Rating, row.Answered, row.Done, row.Escalated).ShouldBe((StoreA, new DateOnly(2026, 10, 19), true, 5, true, false, false));

        // The evaluation of 2026 still knows it (and says so from the figures that were kept).
        var evaluation = await EvaluationAsync(api, Revisor, "?year=2026");
        var figures = FiguresOf(evaluation, StoreA);
        figures.GetProperty("submitted").GetInt32().ShouldBe(1);
        figures.GetProperty("onTime").GetInt32().ShouldBe(1);
        figures.GetProperty("ratingAverage").GetDouble().ShouldBe(5.0);
        figures.GetProperty("answered").GetInt32().ShouldBe(1);
        figures.GetProperty("missing").GetInt32().ShouldBe(51); // 52 weeks in 2026, one of them handed in
        FiguresOf(evaluation, StoreB).GetProperty("submitted").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task The_background_service_applies_the_retention_with_its_rounds()
    {
        await using var api = await StartUpAsync(postgres);
        await HandInAsync(api, Erika, StoreA);
        Clock(api).SetUtcNow(new DateTimeOffset(2027, 11, 20, 12, 0, 0, TimeSpan.Zero));
        var service = api.Services.GetServices<IHostedService>().OfType<LogbookReminderBackgroundService>().Single();

        await service.RunOnceAsync(CancellationToken.None);

        (await InDbAsync(api, db => db.LogbookReports.CountAsync())).ShouldBe(0);
        (await InDbAsync(api, db => db.LogbookWeekStats.CountAsync())).ShouldBe(1);
    }

    [Fact]
    public async Task A_report_that_is_deleted_and_comes_up_again_does_not_double_count()
    {
        await using var api = await StartUpAsync(postgres);
        await HandInAsync(api, Erika, StoreA, rating: 4);
        Clock(api).SetUtcNow(new DateTimeOffset(2027, 11, 20, 12, 0, 0, TimeSpan.Zero));
        await RunRetentionAsync(api);

        // Somebody keeps the figures of the week and a live report for the same week exists (cannot happen through the API, but must not count twice).
        await InDbAsync(api, async db =>
        {
            db.LogbookReports.Add(new LogbookReportRecord
            {
                StoreId = StoreA, Week = new DateOnly(2026, 10, 19), AnswersJson = "{}", Phase = LogbookReportPhase.Submitted, SubmittedAt = Start, SubmittedOnTime = true,
            });
            await db.SaveChangesAsync();
            return 0;
        });

        var figures = FiguresOf(await EvaluationAsync(api, Revisor, "?year=2026"), StoreA);

        figures.GetProperty("submitted").GetInt32().ShouldBe(1); // one week, one report
    }

    [Fact]
    public async Task The_content_of_a_report_does_not_live_on_in_the_audit_log()
    {
        await using var api = await StartUpAsync(postgres);
        var a = await HandInAsync(api, Erika, StoreA);
        using (var reply = await api.SendAsync(HttpMethod.Post, $"/api/logbook/reports/{a}/comments", It, new { kind = "reply", text = SecretText }))
        {
            reply.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var entries = await InDbAsync(api, db => db.Set<AuditEntryRecord>().AsNoTracking().ToListAsync());

        entries.ShouldContain(e => e.EntityType == "LogbookDistrictRecord"); // the audit works, the structure is logged
        entries.ShouldNotContain(e => e.EntityType == "LogbookReportRecord" || e.EntityType == "LogbookCommentRecord");
        entries.ShouldAllBe(e => e.ChangesJson == null || !e.ChangesJson.Contains(SecretText));
    }
}
