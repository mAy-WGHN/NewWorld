using System.Globalization;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Shared.Authorization;
using Shared.Testing;
using Workforce.Api;
using Workforce.Core.Absences;
using Workforce.Core.Employees;
using Workforce.Infrastructure;
using static Workforce.Api.Tests.WorkforceApi;

namespace Workforce.Api.Tests;

/// <summary>Finding people, the active people of a store and "who am I": reach, minimum length, leavers, and who may ask at all.</summary>
public sealed class PeopleEndpointTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static string Search(string query) => $"/api/employees?query={Uri.EscapeDataString(query)}";

    private static async Task SetNumberAsync(WorkforceApi api, Guid person, string number)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkforceDbContext>();
        db.People.Single(p => p.EmployeeId == person).PersonnelNumber = number;
        await db.SaveChangesAsync();
    }

    // ---------------------------------------------------------------- search

    [Fact]
    public async Task A_person_is_found_by_words_of_the_name_or_the_personnel_number_in_any_case()
    {
        await using var api = await StartAsync(postgres);
        await SetNumberAsync(api, Erika, "10042");

        (await api.GetAsync<List<EmployeeSummaryDto>>(Search("MUSTER"), Hanna)).ShouldHaveSingleItem()
            .ShouldBe(new EmployeeSummaryDto(Erika, "10042", "Erika Mustermann", StoreA, null));
        (await api.GetAsync<List<EmployeeSummaryDto>>(Search("10042"), Hanna)).Select(p => p.EmployeeId).ShouldBe([Erika]);
        (await api.GetAsync<List<EmployeeSummaryDto>>(Search("erika must"), Hanna)).Select(p => p.EmployeeId).ShouldBe([Erika]);
        (await api.GetAsync<List<EmployeeSummaryDto>>(Search("erika leiter"), Hanna)).ShouldBeEmpty();
        (await api.GetAsync<List<EmployeeSummaryDto>>(Search("leit"), Hanna)).Select(p => p.EmployeeId).ShouldBe([Lena, Max], ignoreOrder: true);
    }

    [Fact]
    public async Task Wildcard_characters_are_taken_literally_and_a_query_needs_two_characters_unless_a_store_is_given()
    {
        await using var api = await StartAsync(postgres);

        (await api.GetAsync<List<EmployeeSummaryDto>>(Search("%%"), Hanna)).ShouldBeEmpty();
        (await api.GetAsync<List<EmployeeSummaryDto>>(Search("e_"), Hanna)).ShouldBeEmpty();
        (await api.SendAsync(HttpMethod.Get, Search("e"), Hanna)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await api.SendAsync(HttpMethod.Get, "/api/employees", Hanna)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        (await api.GetAsync<List<EmployeeSummaryDto>>($"/api/employees?storeId={StoreB}", Hanna)).Select(p => p.EmployeeId).ShouldBe([Lena]);
    }

    [Fact]
    public async Task Only_people_in_the_reach_of_the_caller_are_found_and_without_the_privilege_nobody()
    {
        await using var api = await StartAsync(postgres);
        api.Grant(Max, new TestGrant(EmployeePrivileges.Read, [StoreA], [Erika, Ben, Max]));

        (await api.GetAsync<List<EmployeeSummaryDto>>(Search("leit"), Max)).Select(p => p.EmployeeId).ShouldBe([Max]); // Lena (store B) stays hidden
        (await api.GetAsync<List<EmployeeSummaryDto>>($"/api/employees?storeId={StoreB}", Max)).ShouldBeEmpty();

        (await api.SendAsync(HttpMethod.Get, Search("muster"), Erika)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await api.SendAsync(HttpMethod.Get, Search("muster"), null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---------------------------------------------------------------- people of a store

    [Fact]
    public async Task The_people_of_a_store_are_listed_without_those_who_have_left_for_everybody_with_the_availability_privilege()
    {
        await using var api = await StartAsync(postgres);
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorkforceDbContext>();
            db.People.Single(p => p.EmployeeId == Max).ExitDate = new DateOnly(2026, 10, 1); // before "today" (2026-10-23)
            await db.SaveChangesAsync();
        }

        var colleagues = await api.GetAsync<List<StorePersonDto>>($"/api/stores/{StoreA}/people", Ben);
        colleagues.ShouldBe([new StorePersonDto(Ben, "Ben Beispiel"), new StorePersonDto(Erika, "Erika Mustermann")]);
        (await api.GetAsync<List<StorePersonDto>>($"/api/stores/{StoreB}/people", Lena)).Select(p => p.EmployeeId).ShouldBe([Lena]);
    }

    [Fact]
    public async Task The_people_of_a_store_are_not_shown_outside_the_reach_or_without_a_privilege()
    {
        await using var api = await StartAsync(postgres);

        (await api.SendAsync(HttpMethod.Get, $"/api/stores/{StoreB}/people", Ben)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await api.SendAsync(HttpMethod.Get, $"/api/stores/{StoreA}/people", Erika)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await api.SendAsync(HttpMethod.Get, $"/api/stores/{StoreA}/people", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---------------------------------------------------------------- who am I

    [Fact]
    public async Task The_profile_shows_name_number_and_main_store_of_the_signed_in_person_only()
    {
        await using var api = await StartAsync(postgres);
        await SetNumberAsync(api, Erika, "10042");

        (await api.GetAsync<ProfileDto>("/api/me/profile", Erika)).ShouldBe(new ProfileDto(Erika, "Erika Mustermann", "10042", new ProfileStoreDto(StoreA, "017", "Store A")));
        (await api.GetAsync<ProfileDto>("/api/me/profile", Lena)).MainStore!.StoreId.ShouldBe(StoreB);
        (await api.SendAsync(HttpMethod.Get, "/api/me/profile", Hanna)).StatusCode.ShouldBe(HttpStatusCode.NotFound); // no copy of this person
        (await api.SendAsync(HttpMethod.Get, "/api/me/profile", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---------------------------------------------------------------- own timeline with a period

    [Fact]
    public async Task The_own_timeline_can_be_limited_to_a_period()
    {
        await using var api = await StartAsync(postgres);
        var kind = (await api.GetAsync<List<UnavailabilityKindDto>>("/api/unavailability-kinds", Hanna)).First(k => !k.IsSystem);
        foreach (var (from, to) in new[] { ("2026-11-02", "2026-11-03"), ("2026-12-07", "2026-12-08") })
        {
            var request = new AddUnavailabilityRequest(kind.Id, DateOnly.Parse(from, CultureInfo.InvariantCulture), DateOnly.Parse(to, CultureInfo.InvariantCulture));
            (await api.SendAsync(HttpMethod.Post, $"/api/employees/{Erika}/unavailabilities", Hanna, request)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await api.GetAsync<List<UnavailabilityDto>>("/api/me/unavailabilities", Erika)).Count.ShouldBe(2);
        (await api.GetAsync<List<UnavailabilityDto>>("/api/me/unavailabilities?from=2026-12-01&to=2026-12-31", Erika)).ShouldHaveSingleItem().From.ShouldBe(new DateOnly(2026, 12, 7));
        (await api.GetAsync<List<UnavailabilityDto>>("/api/me/unavailabilities?from=2026-11-03", Erika)).Count.ShouldBe(2); // an entry touching the period is returned whole
        (await api.GetAsync<List<UnavailabilityDto>>("/api/me/unavailabilities?to=2026-11-01", Erika)).ShouldBeEmpty();
        (await api.GetAsync<List<UnavailabilityDto>>("/api/me/unavailabilities?from=2026-12-09", Erika)).ShouldBeEmpty();
        (await api.SendAsync(HttpMethod.Get, "/api/me/unavailabilities?from=2026-12-09&to=2026-12-01", Erika)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }
}
