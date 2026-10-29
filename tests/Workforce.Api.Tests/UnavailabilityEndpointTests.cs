using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Testing;
using Workforce.Api;
using Workforce.Core.Unavailability;
using Workforce.Infrastructure;
using static Workforce.Api.Tests.WorkforceApi;

namespace Workforce.Api.Tests;

public sealed class UnavailabilityEndpointTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static async Task<UnavailabilityKindDto> KindAsync(WorkforceApi api, string code) =>
        (await api.GetAsync<List<UnavailabilityKindDto>>("/api/unavailability-kinds?includeInactive=true", Hanna)).Single(k => k.Code == code);

    private static async Task<HttpResponseMessage> EnterAsync(WorkforceApi api, Guid? by, Guid employee, Guid kindId, string from, string to) =>
        await api.SendAsync(HttpMethod.Post, $"/api/employees/{employee}/unavailabilities", by, new AddUnavailabilityRequest(kindId, DateOnly.Parse(from, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(to, System.Globalization.CultureInfo.InvariantCulture)));

    // ---------------------------------------------------------------- kinds

    [Fact]
    public async Task The_kinds_are_seeded_and_only_HR_maintains_them()
    {
        await using var api = await StartAsync(postgres);

        var kinds = await api.GetAsync<List<UnavailabilityKindDto>>("/api/unavailability-kinds", Hanna);
        kinds.Count.ShouldBe(24);
        (await KindAsync(api, "KRANK")).ShouldBe(new UnavailabilityKindDto(UnavailabilityKindSeed.All.Single(k => k.Code == "KRANK").Id, "KRANK", "Krank", true, "K", null, true, true));

        // Without privilege: no list, no changes. Not logged in: 401.
        foreach (var person in new Guid?[] { Erika, Max, null })
        {
            var expected = person is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
            (await api.SendAsync(HttpMethod.Get, "/api/unavailability-kinds", person)).StatusCode.ShouldBe(expected);
            (await api.SendAsync(HttpMethod.Post, "/api/unavailability-kinds", person, new AddUnavailabilityKindRequest("X", "x", false, null, null))).StatusCode.ShouldBe(expected);
            (await api.SendAsync(HttpMethod.Put, $"/api/unavailability-kinds/{kinds[0].Id}", person, new UpdateUnavailabilityKindRequest("x", false, null, null, null))).StatusCode.ShouldBe(expected);
        }
    }

    [Fact]
    public async Task HR_adds_changes_and_deactivates_kinds_but_never_deletes_them()
    {
        await using var api = await StartAsync(postgres);

        using var created = await api.SendAsync(HttpMethod.Post, "/api/unavailability-kinds", Hanna, new AddUnavailabilityKindRequest("WEHRÜBUNG", "Wehrübung", false, "WÜ", null));
        created.StatusCode.ShouldBe(HttpStatusCode.OK);
        var kind = (await created.Content.ReadFromJsonAsync<UnavailabilityKindDto>())!;
        (kind.Code, kind.IsSystem, kind.IsActive).ShouldBe(("WEHRÜBUNG", false, true));

        (await api.SendAsync(HttpMethod.Post, "/api/unavailability-kinds", Hanna, new AddUnavailabilityKindRequest("wehrübung", "doppelt", false, null, null))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await api.SendAsync(HttpMethod.Post, "/api/unavailability-kinds", Hanna, new AddUnavailabilityKindRequest("", "leer", false, null, null))).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        (await api.SendAsync(HttpMethod.Put, $"/api/unavailability-kinds/{kind.Id}", Hanna, new UpdateUnavailabilityKindRequest("Wehrübung (Reserve)", true, "WÜ", "W1", false))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await KindAsync(api, "WEHRÜBUNG")).ShouldBe(kind with { Description = "Wehrübung (Reserve)", PaidTime = true, WorkCode = "W1", IsActive = false });
        (await api.GetAsync<List<UnavailabilityKindDto>>("/api/unavailability-kinds", Hanna)).ShouldNotContain(k => k.Code == "WEHRÜBUNG");

        // Deactivated kinds cannot be entered any more.
        (await EnterAsync(api, Hanna, Erika, kind.Id, "2026-11-02", "2026-11-03")).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        // The system kinds stay active; unknown kinds are 404.
        var sick = await KindAsync(api, "KRANK");
        (await api.SendAsync(HttpMethod.Put, $"/api/unavailability-kinds/{sick.Id}", Hanna, new UpdateUnavailabilityKindRequest("Krank", true, "K", null, false))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await api.SendAsync(HttpMethod.Put, $"/api/unavailability-kinds/{Guid.NewGuid()}", Hanna, new UpdateUnavailabilityKindRequest("x", false, null, null, null))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ---------------------------------------------------------------- entries

    [Fact]
    public async Task An_entry_that_reaches_past_a_reported_exit_is_only_marked_for_HR_and_the_person()
    {
        await using var api = await StartAsync(postgres);
        var special = await KindAsync(api, "SONDERURLAUB");
        (await EnterAsync(api, Hanna, Erika, special.Id, "2026-11-02", "2026-11-10")).StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorkforceDbContext>();
            (await db.People.SingleAsync(p => p.EmployeeId == Erika)).ExitDate = new DateOnly(2026, 11, 5); // the exit is reported afterwards
            await db.SaveChangesAsync();
        }

        var forHr = (await api.GetAsync<List<UnavailabilityDto>>($"/api/employees/{Erika}/unavailabilities", Hanna)).Single();
        (forHr.To, forHr.AfterExit).ShouldBe(((DateOnly?)new DateOnly(2026, 11, 10), (bool?)true)); // not cut: only marked
        (await api.GetAsync<List<UnavailabilityDto>>("/api/me/unavailabilities", Erika)).Single().AfterExit.ShouldBe(true);
        // A colleague sees no hint (and not the kind either).
        (await api.GetAsync<List<UnavailabilityDto>>($"/api/employees/{Erika}/unavailabilities", Ben)).Single().AfterExit.ShouldBeNull();
    }

    [Fact]
    public async Task Only_HR_enters_and_removes_entries()
    {
        await using var api = await StartAsync(postgres);
        var special = await KindAsync(api, "SONDERURLAUB");

        // Neither the person, the colleague, the store manager nor somebody not logged in may enter anything.
        foreach (var person in new Guid?[] { Erika, Ben, Max, null })
        {
            (await EnterAsync(api, person, Erika, special.Id, "2026-11-02", "2026-11-03")).StatusCode.ShouldBe(person is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden);
        }

        using var created = await EnterAsync(api, Hanna, Erika, special.Id, "2026-11-02", "2026-11-03");
        created.StatusCode.ShouldBe(HttpStatusCode.OK);
        var entry = (await created.Content.ReadFromJsonAsync<UnavailabilityDto>())!;
        (entry.EmployeeId, entry.From, entry.To, entry.Origin).ShouldBe((Erika, new DateOnly(2026, 11, 2), (DateOnly?)new DateOnly(2026, 11, 3), "Manual"));

        foreach (var person in new Guid?[] { Erika, Ben, Max, null })
        {
            (await api.SendAsync(HttpMethod.Delete, $"/api/employees/{Erika}/unavailabilities/{entry.Id}", person)).StatusCode.ShouldBe(person is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden);
        }

        (await api.SendAsync(HttpMethod.Delete, $"/api/employees/{Erika}/unavailabilities/{entry.Id}", Hanna)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await api.SendAsync(HttpMethod.Delete, $"/api/employees/{Erika}/unavailabilities/{entry.Id}", Hanna)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_reach_limited_grant_enters_entries_only_inside_its_reach()
    {
        await using var api = await StartAsync(postgres);
        api.Grant(Max, new TestGrant(UnavailabilityPrivileges.Create, [StoreA], [Erika, Ben, Max]), new TestGrant(UnavailabilityPrivileges.Delete, [StoreA], [Erika, Ben, Max]));
        var free = await KindAsync(api, "FREI");

        (await EnterAsync(api, Max, Erika, free.Id, "2026-11-02", "2026-11-02")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await EnterAsync(api, Max, Lena, free.Id, "2026-11-02", "2026-11-02")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await api.SendAsync(HttpMethod.Get, "/api/unavailability-kinds", Max)).StatusCode.ShouldBe(HttpStatusCode.OK); // the one who enters needs the list
    }

    [Fact]
    public async Task Invalid_overlapping_and_system_kinds_are_refused()
    {
        await using var api = await StartAsync(postgres);
        var special = await KindAsync(api, "SONDERURLAUB");
        (await EnterAsync(api, Hanna, Erika, special.Id, "2026-11-02", "2026-11-04")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await EnterAsync(api, Hanna, Erika, special.Id, "2026-11-04", "2026-11-05")).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await EnterAsync(api, Hanna, Erika, special.Id, "2026-11-10", "2026-11-09")).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await EnterAsync(api, Hanna, Erika, Guid.NewGuid(), "2026-11-10", "2026-11-11")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await EnterAsync(api, Hanna, Guid.NewGuid(), special.Id, "2026-11-10", "2026-11-11")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        foreach (var code in new[] { "KRANK", "URLAUB", "KRANK I URLAUB" })
        {
            (await EnterAsync(api, Hanna, Erika, (await KindAsync(api, code)).Id, "2026-12-01", "2026-12-02")).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        }
    }

    // ---------------------------------------------------------------- who sees what

    [Fact]
    public async Task The_kind_is_seen_by_the_person_HR_and_supervisors_colleagues_only_see_unavailable()
    {
        await using var api = await StartAsync(postgres);
        var special = await KindAsync(api, "SONDERURLAUB");
        (await EnterAsync(api, Hanna, Erika, special.Id, "2026-11-02", "2026-11-03")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var url = $"/api/employees/{Erika}/unavailabilities";

        // The person sees the entry HR made (the schedule needs it), with its kind.
        var own = (await api.GetAsync<List<UnavailabilityDto>>("/api/me/unavailabilities", Erika)).Single();
        (own.Kind, own.Description, own.ScheduleSymbol, own.Origin).ShouldBe(("SONDERURLAUB", "Sonder-Urlaub", "SU", "Manual"));
        (await api.GetAsync<List<UnavailabilityDto>>(url, Erika)).Single().Kind.ShouldBe("SONDERURLAUB");

        foreach (var privileged in new[] { Hanna, Max })
        {
            (await api.GetAsync<List<UnavailabilityDto>>(url, privileged)).Single().Kind.ShouldBe("SONDERURLAUB");
        }

        // The colleague only learns that Erika is not available.
        var colleague = (await api.GetAsync<List<UnavailabilityDto>>(url, Ben)).Single();
        (colleague.Status, colleague.From, colleague.To).ShouldBe(("Unavailable", new DateOnly(2026, 11, 2), (DateOnly?)new DateOnly(2026, 11, 3)));
        (colleague.Kind, colleague.Description, colleague.ScheduleSymbol, colleague.PaidTime, colleague.Origin, colleague.Id).ShouldBe((null, null, null, null, null, null));

        // Out of reach: nothing, not even that something exists.
        (await api.SendAsync(HttpMethod.Get, url, Lena)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await api.SendAsync(HttpMethod.Get, url, null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_neutral_view_does_not_leak_the_kind_in_the_raw_response()
    {
        await using var api = await StartAsync(postgres);
        var special = await KindAsync(api, "ELTERNZEIT");
        await EnterAsync(api, Hanna, Erika, special.Id, "2026-11-02", "2026-11-03");

        using var response = await api.SendAsync(HttpMethod.Get, $"/api/stores/{StoreA}/unavailabilities?from=2026-11-01&to=2026-11-30", Ben);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("Erika Mustermann");
        body.ShouldNotContain("ELTERNZEIT", Case.Insensitive);
        body.ShouldNotContain("Elternzeit", Case.Insensitive);
    }

    [Fact]
    public async Task The_store_view_shows_the_kind_for_supervisors_and_is_closed_to_other_stores()
    {
        await using var api = await StartAsync(postgres);
        var special = await KindAsync(api, "SONDERURLAUB");
        await EnterAsync(api, Hanna, Erika, special.Id, "2026-11-02", "2026-11-03");
        var url = $"/api/stores/{StoreA}/unavailabilities?from=2026-11-01&to=2026-11-30";

        (await api.GetAsync<List<StoreUnavailabilityDto>>(url, Max)).Single().Period.Kind.ShouldBe("SONDERURLAUB");
        (await api.GetAsync<List<StoreUnavailabilityDto>>(url, Hanna)).Single().Period.Kind.ShouldBe("SONDERURLAUB");
        var colleague = (await api.GetAsync<List<StoreUnavailabilityDto>>(url, Ben)).Single();
        (colleague.EmployeeName, colleague.Period.Kind).ShouldBe(("Erika Mustermann", null));
        (await api.GetAsync<List<StoreUnavailabilityDto>>($"/api/stores/{StoreA}/unavailabilities?from=2026-12-01&to=2026-12-31", Hanna)).ShouldBeEmpty();

        (await api.SendAsync(HttpMethod.Get, url, Lena)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await api.SendAsync(HttpMethod.Get, url, Erika)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await api.SendAsync(HttpMethod.Get, url, null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await api.SendAsync(HttpMethod.Get, $"/api/stores/{StoreA}/unavailabilities?from=2026-01-01&to=2028-01-01", Hanna)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    // ---------------------------------------------------------------- absences in the timeline, KU

    [Fact]
    public async Task Sickness_inside_approved_vacation_shows_as_KU_in_the_timeline_also_for_colleagues_of_the_same_schedule()
    {
        await using var api = await StartAsync(postgres);
        await api.SetEntitlementAsync(Erika, 2026, 30);
        var vacation = await api.PostAbsenceAsync("/api/me/absences/vacation", Erika, new RequestVacationRequest(new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 13)));
        await api.PostAbsenceAsync($"/api/absences/{vacation.Id}/approve", Max);
        await api.PostAbsenceAsync("/api/me/absences/sick", Erika, new ReportSickRequest(new DateOnly(2026, 11, 4), new DateOnly(2026, 11, 5)));

        var own = await api.GetAsync<List<UnavailabilityDto>>("/api/me/unavailabilities", Erika);

        own.Select(i => (i.Kind, i.From, i.To)).ShouldBe([
            ("URLAUB", new DateOnly(2026, 11, 2), (DateOnly?)new DateOnly(2026, 11, 3)),
            ("KRANK I URLAUB", new DateOnly(2026, 11, 4), new DateOnly(2026, 11, 5)),
            ("URLAUB", new DateOnly(2026, 11, 6), new DateOnly(2026, 11, 13))]);
        own.Select(i => i.ScheduleSymbol).ShouldBe(["U", "KU", "U"]);

        // The colleague sees what the schedule shows: vacation and KU as such, but nothing that is not shown (no id, no origin).
        var colleague = await api.GetAsync<List<UnavailabilityDto>>($"/api/employees/{Erika}/unavailabilities", Ben);
        colleague.Select(i => (i.Kind, i.ScheduleSymbol)).ShouldBe([("URLAUB", "U"), ("KRANK I URLAUB", "KU"), ("URLAUB", "U")]);
        colleague.ShouldAllBe(i => i.Id == null && i.Origin == null && i.PaidTime == null);
        (await api.SendAsync(HttpMethod.Get, $"/api/employees/{Erika}/unavailabilities", Lena)).StatusCode.ShouldBe(HttpStatusCode.Forbidden); // not in this schedule: nothing
    }

    [Fact]
    public async Task Colleagues_see_sickness_by_name_but_every_other_kind_only_as_unavailable()
    {
        await using var api = await StartAsync(postgres);
        await api.PostAbsenceAsync("/api/me/absences/sick", Erika, new ReportSickRequest(new DateOnly(2026, 10, 23), new DateOnly(2026, 10, 24)));
        await EnterAsync(api, Hanna, Erika, (await KindAsync(api, "REHA")).Id, "2026-11-02", "2026-11-03");
        await EnterAsync(api, Hanna, Erika, (await KindAsync(api, "FREI")).Id, "2026-11-04", "2026-11-04");

        var colleague = await api.GetAsync<List<UnavailabilityDto>>($"/api/employees/{Erika}/unavailabilities", Ben);

        colleague.Select(i => (i.From, i.To, i.Kind)).ShouldBe([
            (new DateOnly(2026, 10, 23), (DateOnly?)new DateOnly(2026, 10, 24), (string?)"KRANK"),
            (new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 4), null)]); // REHA and FREI touch: one neutral period
    }

    [Fact]
    public async Task A_vacation_request_over_an_entry_of_HR_is_refused_with_overlap()
    {
        await using var api = await StartAsync(postgres);
        await api.SetEntitlementAsync(Erika, 2026, 30);
        await EnterAsync(api, Hanna, Erika, (await KindAsync(api, "FORTBILDUNG")).Id, "2026-11-03", "2026-11-04");

        using var response = await api.SendAsync(HttpMethod.Post, "/api/me/absences/vacation", Erika, new RequestVacationRequest(new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 3)));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }
}
