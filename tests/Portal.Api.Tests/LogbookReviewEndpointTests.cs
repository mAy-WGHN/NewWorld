using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Portal.Core;
using Portal.Core.Logbook;
using Portal.Infrastructure;
using Portal.Infrastructure.Messaging;
using Shared.Testing;
using static Portal.Api.Tests.PortalApi;

namespace Portal.Api.Tests;

/// <summary>
/// What the regional and district leaders do with the weekly reports (decision 49): read the reports of their stores, reply and comment internally, set the
/// state, and see that the store manager followed up. The visibility matrix is checked for every role. Clock: Friday 2026-10-23, 12:00 UTC, current week
/// 2026-10-19.
/// </summary>
public sealed class LogbookReviewEndpointTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string ThisWeek = "2026-10-19";

    private static readonly Guid DistrictSouth = Guid.Parse("00000000-0000-0000-0000-0000000000d1");
    private static readonly Guid RegionAugsburg = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid RegionMunich = Guid.Parse("00000000-0000-0000-0000-0000000000a2");

    // Erika: store manager of A. Hanna: store manager of C (and administration). It and Mia: regional leaders of Augsburg (A, B). Nils: regional leader of Munich (C).
    // Revisor: district leader of the South. Ben holds the review privilege but leads nothing.
    private static readonly Guid Mia = Guid.Parse("00000000-0000-0000-0000-0000000000a9");
    private static readonly Guid Nils = Guid.Parse("00000000-0000-0000-0000-0000000000aa");
    private static readonly Guid Fiona = Guid.Parse("00000000-0000-0000-0000-0000000000ab");

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

        // Hanna: administration for the structure, store manager of C only (so that she can write the report of C).
        api.Grant(Hanna, TestGrant.Everywhere(PortalPrivileges.LogbookManageStructure), new TestGrant(PortalPrivileges.LogbookWrite, [StoreC]));
        return api;
    }

    private static LogbookSection Section(int index) => LogbookTemplate.Default.Sections[index];

    private static Dictionary<string, object> Complete(string text = "Alles ruhig.") => new()
    {
        [Section(0).Id.ToString()] = new { optionIds = new[] { Section(0).Options[0].Id }, text = (string?)null },
        [Section(1).Id.ToString()] = new { optionIds = new[] { Section(1).Options[0].Id }, text = (string?)null },
        [Section(3).Id.ToString()] = new { optionIds = Array.Empty<Guid>(), text = (string?)text },
        [Section(5).Id.ToString()] = new { optionIds = new[] { Section(5).Options[0].Id }, text = (string?)null },
    };

    /// <summary>The store manager hands in a complete report for the current week; returns its id.</summary>
    private static async Task<Guid> SubmittedAsync(PortalApi api, Guid manager, Guid store, string text = "Alles ruhig.")
    {
        (await api.SendAsync(HttpMethod.Put, $"/api/logbook/reports/{store}/{ThisWeek}", manager, new { answers = Complete(text) })).StatusCode.ShouldBe(HttpStatusCode.OK);
        using var response = await api.SendAsync(HttpMethod.Post, $"/api/logbook/reports/{store}/{ThisWeek}/submit", manager);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await DataAsync(response)).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> DetailAsync(PortalApi api, Guid person, Guid report)
    {
        using var response = await api.SendAsync(HttpMethod.Get, $"/api/logbook/reports/{report}", person);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await DataAsync(response);
    }

    private static async Task<JsonElement> CommentAsync(PortalApi api, Guid person, Guid report, string kind, string text)
    {
        using var response = await api.SendAsync(HttpMethod.Post, $"/api/logbook/reports/{report}/comments", person, new { kind, text });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await DataAsync(response);
    }

    private static async Task<List<JsonElement>> ListAsync(PortalApi api, Guid person, string query = "")
    {
        using var response = await api.SendAsync(HttpMethod.Get, "/api/logbook/reports" + query, person);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return [.. (await DataAsync(response)).EnumerateArray()];
    }

    private static List<string> Texts(JsonElement detail) => [.. detail.GetProperty("comments").EnumerateArray().Select(c => c.GetProperty("text").GetString()!)];

    private static string State(JsonElement detail) => detail.GetProperty("state").GetString()!;

    // ------------------------------------------------------------------ which reports a leader sees

    [Fact]
    public async Task A_regional_leader_sees_the_handed_in_reports_of_the_region_a_district_leader_all_of_the_district()
    {
        await using var api = await StartUpAsync(postgres);
        var a = await SubmittedAsync(api, Erika, StoreA);
        var c = await SubmittedAsync(api, Hanna, StoreC);

        (await ListAsync(api, It)).Select(r => r.GetProperty("id").GetGuid()).ShouldBe([a]);
        (await ListAsync(api, Mia)).Select(r => r.GetProperty("id").GetGuid()).ShouldBe([a]); // the second regional leader of the region
        (await ListAsync(api, Nils)).Select(r => r.GetProperty("id").GetGuid()).ShouldBe([c]);
        (await ListAsync(api, Revisor)).Select(r => r.GetProperty("id").GetGuid()).ShouldBe([a, c], ignoreOrder: true);
        (await ListAsync(api, Ben)).ShouldBeEmpty(); // holds the privilege, leads nothing
    }

    [Fact]
    public async Task A_draft_is_not_visible_to_the_leaders_before_it_is_handed_in()
    {
        await using var api = await StartUpAsync(postgres);
        (await api.SendAsync(HttpMethod.Put, $"/api/logbook/reports/{StoreA}/{ThisWeek}", Erika, new { answers = Complete() })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var draft = (await DataAsync(await api.SendAsync(HttpMethod.Get, "/api/logbook/my-reports", Erika))).GetProperty("stores")[0].GetProperty("report").GetProperty("id").GetGuid();

        (await ListAsync(api, It)).ShouldBeEmpty();
        (await api.SendAsync(HttpMethod.Get, $"/api/logbook/reports/{draft}", It)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await DetailAsync(api, Erika, draft)).GetProperty("phase").GetString().ShouldBe("draft"); // the store manager sees the own draft
    }

    [Fact]
    public async Task A_report_outside_the_reach_is_not_found_for_reading_commenting_and_the_state()
    {
        await using var api = await StartUpAsync(postgres);
        var c = await SubmittedAsync(api, Hanna, StoreC);

        (await api.SendAsync(HttpMethod.Get, $"/api/logbook/reports/{c}", It)).StatusCode.ShouldBe(HttpStatusCode.NotFound); // other region
        (await api.SendAsync(HttpMethod.Get, $"/api/logbook/reports/{c}", Erika)).StatusCode.ShouldBe(HttpStatusCode.NotFound); // other store
        (await api.SendAsync(HttpMethod.Post, $"/api/logbook/reports/{c}/comments", It, new { kind = "reply", text = "x" })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await api.SendAsync(HttpMethod.Put, $"/api/logbook/reports/{c}/state", It, new { state = "done" })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await api.SendAsync(HttpMethod.Get, $"/api/logbook/reports/{Guid.NewGuid()}", It)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_list_can_be_filtered_by_week_store_region_state_and_open_and_is_sorted_with_the_followed_up_first()
    {
        await using var api = await StartUpAsync(postgres);
        var a = await SubmittedAsync(api, Erika, StoreA);
        var b = await SubmittedAsync(api, Fiona, StoreB);
        var c = await SubmittedAsync(api, Hanna, StoreC);
        (await api.SendAsync(HttpMethod.Put, $"/api/logbook/reports/{b}/state", Revisor, new { state = "done" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await api.SendAsync(HttpMethod.Post, $"/api/logbook/reports/{c}/follow-up", Hanna, new { note = "Erledigt" })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var all = await ListAsync(api, Revisor);
        all.Select(r => r.GetProperty("id").GetGuid()).ShouldBe([c, a, b]); // followed up, new, done
        all.Select(r => r.GetProperty("state").GetString()).ShouldBe(["followedUp", "new", "done"]);

        (await ListAsync(api, Revisor, $"?storeId={StoreA}")).Select(r => r.GetProperty("id").GetGuid()).ShouldBe([a]);
        (await ListAsync(api, Revisor, $"?regionId={RegionAugsburg}")).Select(r => r.GetProperty("id").GetGuid()).ShouldBe([a, b], ignoreOrder: true);
        (await ListAsync(api, Revisor, "?state=done")).Select(r => r.GetProperty("id").GetGuid()).ShouldBe([b]);
        (await ListAsync(api, Revisor, "?open=true")).Select(r => r.GetProperty("id").GetGuid()).ShouldBe([c, a]);
        (await ListAsync(api, Revisor, "?week=2026-10-12")).ShouldBeEmpty(); // nothing handed in that week
        (await ListAsync(api, It, $"?storeId={StoreC}")).ShouldBeEmpty(); // a filter never widens the reach
    }

    // ------------------------------------------------------------------ comments and who sees them

    [Fact]
    public async Task The_visibility_matrix_holds_for_every_role()
    {
        await using var api = await StartUpAsync(postgres);
        var report = await SubmittedAsync(api, Erika, StoreA);
        await CommentAsync(api, It, report, "reply", "Antwort der RL It");
        await CommentAsync(api, It, report, "internal", "Intern der RL It");
        await CommentAsync(api, Mia, report, "internal", "Intern der RL Mia");
        await CommentAsync(api, Revisor, report, "reply", "Antwort der BL");
        await CommentAsync(api, Revisor, report, "internal", "Intern der BL");

        Texts(await DetailAsync(api, Erika, report)).ShouldBe(["Antwort der RL It"]); // the store manager: only the reply of the regional leader
        Texts(await DetailAsync(api, It, report)).ShouldBe(["Antwort der RL It", "Intern der RL It", "Antwort der BL"]); // own internal, replies of both levels
        Texts(await DetailAsync(api, Mia, report)).ShouldBe(["Antwort der RL It", "Intern der RL Mia", "Antwort der BL"]);
        Texts(await DetailAsync(api, Revisor, report)).ShouldBe(["Antwort der RL It", "Intern der RL It", "Intern der RL Mia", "Antwort der BL", "Intern der BL"]);
    }

    [Fact]
    public async Task A_comment_knows_its_author_the_level_it_was_written_at_and_whether_it_is_the_own()
    {
        await using var api = await StartUpAsync(postgres);
        var report = await SubmittedAsync(api, Erika, StoreA);

        var regional = await CommentAsync(api, It, report, "reply", "Von der RL");
        var district = await CommentAsync(api, Revisor, report, "reply", "Von der BL");

        regional.GetProperty("kind").GetString().ShouldBe("reply");
        regional.GetProperty("level").GetString().ShouldBe("regional");
        regional.GetProperty("authorName").GetString().ShouldBe("Ingo IT");
        regional.GetProperty("createdAt").GetDateTimeOffset().ShouldBe(Start);
        district.GetProperty("level").GetString().ShouldBe("district");
        var seenByIt = (await DetailAsync(api, It, report)).GetProperty("comments").EnumerateArray().ToList();
        seenByIt.Single(c => c.GetProperty("text").GetString() == "Von der RL").GetProperty("own").GetBoolean().ShouldBeTrue();
        seenByIt.Single(c => c.GetProperty("text").GetString() == "Von der BL").GetProperty("own").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Only_leaders_of_the_store_write_comments_and_the_text_is_checked()
    {
        await using var api = await StartUpAsync(postgres);
        var report = await SubmittedAsync(api, Erika, StoreA);

        foreach (var person in new Guid?[] { Erika, Hanna, null })
        {
            var expected = person is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
            (await api.SendAsync(HttpMethod.Post, $"/api/logbook/reports/{report}/comments", person, new { kind = "reply", text = "x" })).StatusCode.ShouldBe(expected);
        }

        foreach (var body in new object[] { new { kind = "reply", text = " " }, new { kind = "reply", text = new string('x', LogbookLimits.MaxText + 1) }, new { kind = "shout", text = "x" }, new { kind = (string?)null, text = "x" } })
        {
            (await api.SendAsync(HttpMethod.Post, $"/api/logbook/reports/{report}/comments", It, body)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        }

        (await DetailAsync(api, It, report)).GetProperty("comments").GetArrayLength().ShouldBe(0);
    }

    // ------------------------------------------------------------------ the state

    [Fact]
    public async Task A_reply_of_the_regional_leader_puts_the_report_in_progress_but_the_district_leader_and_internal_comments_do_not()
    {
        await using var api = await StartUpAsync(postgres);
        var report = await SubmittedAsync(api, Erika, StoreA);
        State(await DetailAsync(api, It, report)).ShouldBe("new");

        await CommentAsync(api, Revisor, report, "reply", "BL antwortet");
        await CommentAsync(api, It, report, "internal", "Intern");
        State(await DetailAsync(api, It, report)).ShouldBe("new");

        await CommentAsync(api, It, report, "reply", "RL antwortet");
        State(await DetailAsync(api, It, report)).ShouldBe("inProgress");
    }

    [Fact]
    public async Task A_leader_sets_in_progress_or_done_and_nothing_else_and_the_store_manager_cannot()
    {
        await using var api = await StartUpAsync(postgres);
        var report = await SubmittedAsync(api, Erika, StoreA);

        (await api.SendAsync(HttpMethod.Put, $"/api/logbook/reports/{report}/state", It, new { state = "done" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        State(await DetailAsync(api, Erika, report)).ShouldBe("done");
        (await api.SendAsync(HttpMethod.Put, $"/api/logbook/reports/{report}/state", Revisor, new { state = "inProgress" })).StatusCode.ShouldBe(HttpStatusCode.OK); // reopen

        foreach (var state in new[] { "new", "followedUp", "unknown", "" })
        {
            (await api.SendAsync(HttpMethod.Put, $"/api/logbook/reports/{report}/state", It, new { state })).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        }

        foreach (var person in new Guid?[] { Erika, null })
        {
            var expected = person is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
            (await api.SendAsync(HttpMethod.Put, $"/api/logbook/reports/{report}/state", person, new { state = "done" })).StatusCode.ShouldBe(expected);
        }
    }

    // ------------------------------------------------------------------ the follow up

    [Fact]
    public async Task The_store_manager_marks_the_report_as_followed_up_with_a_note_and_the_leader_sees_it()
    {
        await using var api = await StartUpAsync(postgres);
        var report = await SubmittedAsync(api, Erika, StoreA);
        await CommentAsync(api, It, report, "reply", "Bitte kümmere dich um die Kasse.");
        api.Services.GetRequiredService<FakeTimeProvider>().Advance(TimeSpan.FromDays(1));

        using var response = await api.SendAsync(HttpMethod.Post, $"/api/logbook/reports/{report}/follow-up", Erika, new { note = "Kasse ist repariert." });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var seenByLeader = await DetailAsync(api, It, report);
        State(seenByLeader).ShouldBe("followedUp");
        seenByLeader.GetProperty("followUpNote").GetString().ShouldBe("Kasse ist repariert.");
        seenByLeader.GetProperty("followedUpAt").GetDateTimeOffset().ShouldBe(Start.AddDays(1));
        seenByLeader.GetProperty("followedUpByName").GetString().ShouldBe("Erika Mustermann");
        (await DetailAsync(api, Revisor, report)).GetProperty("state").GetString().ShouldBe("followedUp"); // the district leader sees it too
    }

    [Fact]
    public async Task A_follow_up_needs_a_note_and_a_handed_in_report_that_is_not_done_and_only_the_store_manager_of_the_store()
    {
        await using var api = await StartUpAsync(postgres);
        var report = await SubmittedAsync(api, Erika, StoreA);

        foreach (var note in new[] { "", "  ", new string('x', 1001) })
        {
            (await api.SendAsync(HttpMethod.Post, $"/api/logbook/reports/{report}/follow-up", Erika, new { note })).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        }

        (await api.SendAsync(HttpMethod.Post, $"/api/logbook/reports/{report}/follow-up", Fiona, new { note = "x" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden); // store B only
        (await api.SendAsync(HttpMethod.Post, $"/api/logbook/reports/{report}/follow-up", It, new { note = "x" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden); // leaders do not follow up
        (await api.SendAsync(HttpMethod.Post, $"/api/logbook/reports/{report}/follow-up", null, new { note = "x" })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        (await api.SendAsync(HttpMethod.Put, $"/api/logbook/reports/{report}/state", It, new { state = "done" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await api.SendAsync(HttpMethod.Post, $"/api/logbook/reports/{report}/follow-up", Erika, new { note = "Zu spät" })).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        State(await DetailAsync(api, It, report)).ShouldBe("done");
    }

    [Fact]
    public async Task A_draft_cannot_be_followed_up_and_a_new_reply_of_the_regional_leader_reopens_a_followed_up_report()
    {
        await using var api = await StartUpAsync(postgres);
        (await api.SendAsync(HttpMethod.Put, $"/api/logbook/reports/{StoreA}/{ThisWeek}", Erika, new { answers = Complete() })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var draft = (await DataAsync(await api.SendAsync(HttpMethod.Get, "/api/logbook/my-reports", Erika))).GetProperty("stores")[0].GetProperty("report").GetProperty("id").GetGuid();
        (await api.SendAsync(HttpMethod.Post, $"/api/logbook/reports/{draft}/follow-up", Erika, new { note = "x" })).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await api.SendAsync(HttpMethod.Post, $"/api/logbook/reports/{StoreA}/{ThisWeek}/submit", Erika);
        await api.SendAsync(HttpMethod.Post, $"/api/logbook/reports/{draft}/follow-up", Erika, new { note = "Erledigt" });
        State(await DetailAsync(api, It, draft)).ShouldBe("followedUp");

        await CommentAsync(api, It, draft, "reply", "Danke, noch eine Frage.");

        State(await DetailAsync(api, It, draft)).ShouldBe("inProgress");
    }

    // ------------------------------------------------------------------ revised since the reply

    [Fact]
    public async Task The_leader_sees_that_the_report_was_changed_since_the_own_reply()
    {
        await using var api = await StartUpAsync(postgres);
        var clock = api.Services.GetRequiredService<FakeTimeProvider>();
        var report = await SubmittedAsync(api, Erika, StoreA);
        (await DetailAsync(api, It, report)).GetProperty("revisedSinceMyReply").GetBoolean().ShouldBeFalse(); // no reply yet

        clock.Advance(TimeSpan.FromHours(1));
        await CommentAsync(api, It, report, "reply", "Bitte ergänzen.");
        (await DetailAsync(api, It, report)).GetProperty("revisedSinceMyReply").GetBoolean().ShouldBeFalse();

        clock.Advance(TimeSpan.FromHours(1));
        (await api.SendAsync(HttpMethod.Put, $"/api/logbook/reports/{StoreA}/{ThisWeek}", Erika, new { answers = Complete("Ergänzt") })).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await DetailAsync(api, It, report)).GetProperty("revisedSinceMyReply").GetBoolean().ShouldBeTrue();
        (await ListAsync(api, It)).Single().GetProperty("revisedSinceMyReply").GetBoolean().ShouldBeTrue();
        (await DetailAsync(api, Mia, report)).GetProperty("revisedSinceMyReply").GetBoolean().ShouldBeFalse(); // Mia never replied: nothing to be "since"

        clock.Advance(TimeSpan.FromHours(1));
        await CommentAsync(api, It, report, "reply", "Danke.");
        (await DetailAsync(api, It, report)).GetProperty("revisedSinceMyReply").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task The_detail_carries_the_answers_the_template_version_and_the_names()
    {
        await using var api = await StartUpAsync(postgres);
        var report = await SubmittedAsync(api, Erika, StoreA, "Ein Text");

        var detail = await DetailAsync(api, It, report);

        detail.GetProperty("storeName").GetString().ShouldBe("Store A");
        detail.GetProperty("week").GetString().ShouldBe(ThisWeek);
        detail.GetProperty("templateVersion").GetInt32().ShouldBe(0);
        detail.GetProperty("answers").GetProperty(Section(3).Id.ToString()).GetProperty("text").GetString().ShouldBe("Ein Text");
        detail.GetProperty("submittedByName").GetString().ShouldBe("Erika Mustermann");
        detail.GetProperty("viewerLevel").GetString().ShouldBe("regional");
        (await DetailAsync(api, Revisor, report)).GetProperty("viewerLevel").GetString().ShouldBe("district");
        (await DetailAsync(api, Erika, report)).GetProperty("viewerLevel").GetString().ShouldBe("none");
    }

    [Fact]
    public async Task Without_the_review_privilege_nobody_lists_and_anonymous_is_unauthorized()
    {
        await using var api = await StartUpAsync(postgres);
        await SubmittedAsync(api, Erika, StoreA);

        (await api.SendAsync(HttpMethod.Get, "/api/logbook/reports", Erika)).StatusCode.ShouldBe(HttpStatusCode.Forbidden); // store manager, not a reviewer
        (await api.SendAsync(HttpMethod.Get, "/api/logbook/reports", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await api.SendAsync(HttpMethod.Get, $"/api/logbook/reports/{Guid.NewGuid()}", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task When_a_leader_is_erased_the_comments_stay_with_an_unknown_author_name()
    {
        await using var api = await StartUpAsync(postgres);
        var report = await SubmittedAsync(api, Erika, StoreA);
        await CommentAsync(api, It, report, "reply", "Antwort");

        await api.DeliverAsync(new BcIntegration.Contracts.EmployeeDataErasureRequested(Guid.NewGuid(), Guid.NewGuid(), Start, It));

        var comment = (await DetailAsync(api, Revisor, report)).GetProperty("comments")[0];
        comment.GetProperty("text").GetString().ShouldBe("Antwort");
        comment.GetProperty("authorName").GetString().ShouldBe("(unbekannt)");
        using var scope = api.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<PortalDbContext>().LogbookComments.CountAsync()).ShouldBe(1);
    }
}
