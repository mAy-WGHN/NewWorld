using System.Net;
using System.Net.Http.Json;
using BcIntegration.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Testing;
using Workforce.Api;
using Workforce.Core.Employees;
using Workforce.Infrastructure;
using static Workforce.Api.Tests.WorkforceApi;

namespace Workforce.Api.Tests;

/// <summary>The employee card: creating with a personnel number from the number series, who sees which part, the selection lists.</summary>
public sealed class EmployeeEndpointTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static async Task<Dictionary<string, Guid>> SeedListsAsync(WorkforceApi api)
    {
        var ids = new Dictionary<string, Guid>();
        foreach (var (kind, code, name) in new[] { ("Function", "KASSE", "Kassierer"), ("WageType", "L1", "Stundenlohn"), ("Region", "SUED", "Süd") })
        {
            using var response = await api.SendAsync(HttpMethod.Post, $"/api/employee-lists/{kind}", Hanna, new { code, name });
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            ids[kind] = (await response.Content.ReadFromJsonAsync<ReferenceEntryDto>())!.Id;
        }

        return ids;
    }

    private static Dictionary<string, object?> Card(Dictionary<string, Guid> lists, Action<Dictionary<string, object?>>? change = null)
    {
        var card = new Dictionary<string, object?>
        {
            ["firstName"] = "Erika", ["lastName"] = "Neu", ["gender"] = "Female", ["birthDate"] = "1990-05-17",
            ["street"] = "Hauptstr. 1", ["postalCode"] = "86150", ["city"] = "Augsburg", ["privatePhone"] = "+49 821 123456", ["privateEmail"] = "erika@privat.example",
            ["businessEmail"] = "erika@firma.example", ["mainStoreId"] = StoreA, ["functionId"] = lists["Function"], ["wageTypeId"] = lists["WageType"],
            ["hireDate"] = "2026-12-01", ["qtradoExport"] = false, ["vacationDaysPerYear"] = 25,
        };
        change?.Invoke(card);
        return card;
    }

    private static async Task<CreatedEmployeeDto> CreateAsync(WorkforceApi api, object body, Guid by)
    {
        using var response = await api.SendAsync(HttpMethod.Post, "/api/employees", by, body);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<CreatedEmployeeDto>())!;
    }

    private static async Task SetSeriesAsync(WorkforceApi api, long last) =>
        (await api.SendAsync(HttpMethod.Put, "/api/personnel-number-series", Hanna, new { lastNumber = last })).StatusCode.ShouldBe(HttpStatusCode.OK);

    // ---------------------------------------------------------------- creating

    [Fact]
    public async Task Nobody_is_created_before_HR_has_entered_the_last_used_number_then_the_numbers_follow_on()
    {
        await using var api = await StartAsync(postgres);
        var lists = await SeedListsAsync(api);

        (await api.SendAsync(HttpMethod.Post, "/api/employees", Hanna, Card(lists))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await api.GetAsync<NumberSeriesDto>("/api/personnel-number-series", Hanna)).LastNumber.ShouldBeNull();

        await SetSeriesAsync(api, 10041);
        var first = await CreateAsync(api, Card(lists), Hanna);
        var second = await CreateAsync(api, Card(lists, c => c["firstName"] = "Max"), Hanna);

        (first.PersonnelNumber, second.PersonnelNumber).ShouldBe(("10042", "10043"));
        (await api.GetAsync<NumberSeriesDto>("/api/personnel-number-series", Hanna)).LastNumber.ShouldBe(10043);
    }

    [Fact]
    public async Task Parallel_creations_never_get_the_same_number()
    {
        await using var api = await StartAsync(postgres);
        var lists = await SeedListsAsync(api);
        await SetSeriesAsync(api, 10041);

        var created = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => CreateAsync(api, Card(lists, c => c["firstName"] = "P" + i), Hanna)));

        created.Select(c => c.PersonnelNumber).Distinct().Count().ShouldBe(8);
        created.Select(c => long.Parse(c.PersonnelNumber, System.Globalization.CultureInfo.InvariantCulture)).Order().ShouldBe(Enumerable.Range(10042, 8).Select(n => (long)n));
    }

    [Fact]
    public async Task A_failed_creation_does_not_use_up_a_number()
    {
        await using var api = await StartAsync(postgres);
        var lists = await SeedListsAsync(api);
        await SetSeriesAsync(api, 10041);

        (await api.SendAsync(HttpMethod.Post, "/api/employees", Hanna, Card(lists, c => c["privateEmail"] = "not-an-email"))).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await api.SendAsync(HttpMethod.Post, "/api/employees", Hanna, Card(lists, c => c["gender"] = "Robot"))).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await api.SendAsync(HttpMethod.Post, "/api/employees", Hanna, Card(lists, c => c["mainStoreId"] = Guid.NewGuid()))).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity); // an unknown store

        (await CreateAsync(api, Card(lists), Hanna)).PersonnelNumber.ShouldBe("10042");
    }

    [Fact]
    public async Task The_new_employee_is_known_to_the_service_at_once_and_gets_the_pro_rata_vacation()
    {
        await using var api = await StartAsync(postgres);
        var lists = await SeedListsAsync(api);
        await SetSeriesAsync(api, 10041);

        var created = await CreateAsync(api, Card(lists), Hanna);

        // Hired on 1 December: one full month, 25 / 12 * 1 = 2.083 -> 2.5 days.
        var balance = await api.GetAsync<VacationBalanceDto>($"/api/employees/{created.EmployeeId}/vacation-balance?year=2026", Hanna);
        (balance.ContractualAnnualDays, balance.Entitlement).ShouldBe((25m, 2.5m));
        using var scope = api.Services.CreateScope();
        var person = await scope.ServiceProvider.GetRequiredService<WorkforceDbContext>().People.AsNoTracking().SingleAsync(p => p.EmployeeId == created.EmployeeId);
        (person.PersonnelNumber, person.Name, person.HomeStoreId, person.EntryDate).ShouldBe(("10042", "Erika Neu", StoreA, new DateOnly(2026, 12, 1)));
    }

    [Fact]
    public async Task A_number_from_BC_above_the_last_one_raises_the_series()
    {
        await using var api = await StartAsync(postgres);
        var lists = await SeedListsAsync(api);
        await SetSeriesAsync(api, 10041);

        await api.DeliverAsync(new EmployeeChanged(Guid.NewGuid(), Guid.NewGuid(), Start, Guid.NewGuid(), "10100", "Direkt In BC", StoreA, null, null, null, new DateOnly(2026, 10, 1), null));
        await api.DeliverAsync(new EmployeeChanged(Guid.NewGuid(), Guid.NewGuid(), Start, Guid.NewGuid(), "9000", "Alt", StoreA, null, null, null, new DateOnly(2010, 1, 1), null));

        (await api.GetAsync<NumberSeriesDto>("/api/personnel-number-series", Hanna)).LastNumber.ShouldBe(10100);
        (await CreateAsync(api, Card(lists), Hanna)).PersonnelNumber.ShouldBe("10101");
    }

    [Fact]
    public async Task Who_may_create_depends_on_the_privilege_and_the_store()
    {
        await using var api = await StartAsync(postgres);
        var lists = await SeedListsAsync(api);
        await SetSeriesAsync(api, 10041);

        (await api.SendAsync(HttpMethod.Post, "/api/employees", Max, Card(lists))).StatusCode.ShouldBe(HttpStatusCode.Forbidden); // no privilege
        (await api.SendAsync(HttpMethod.Post, "/api/employees", null, Card(lists))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        api.Grant(Max, new TestGrant(EmployeePrivileges.Create, [StoreA], [Max]));
        (await api.SendAsync(HttpMethod.Post, "/api/employees", Max, Card(lists, c => c.Remove("wageTypeId")))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await api.SendAsync(HttpMethod.Post, "/api/employees", Max, Card(lists, c => c["mainStoreId"] = StoreB))).StatusCode.ShouldBe(HttpStatusCode.Forbidden); // another store
        (await api.SendAsync(HttpMethod.Post, "/api/employees", Max, Card(lists, c => c["mainStoreId"] = null))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);  // no store needs unrestricted
        (await api.SendAsync(HttpMethod.Post, "/api/employees", Max, Card(lists))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);                                // wage type needs its own privilege

        // HR (unrestricted) may create head office staff without a store.
        await CreateAsync(api, Card(lists, c => c["mainStoreId"] = null), Hanna);
    }

    // ---------------------------------------------------------------- who sees what

    [Fact]
    public async Task Private_data_and_wage_type_are_shown_only_to_those_who_may_see_them()
    {
        await using var api = await StartAsync(postgres);
        var lists = await SeedListsAsync(api);
        await SetSeriesAsync(api, 10041);
        var created = await CreateAsync(api, Card(lists), Hanna);
        var url = $"/api/employees/{created.EmployeeId}/card";

        // HR sees everything.
        var hr = await api.GetAsync<EmployeeCardDto>(url, Hanna);
        (hr.PersonnelNumber, hr.FirstName, hr.State, hr.Private?.City, hr.Wage?.WageTypeId).ShouldBe(("10042", "Erika", "PendingBc", "Augsburg", lists["WageType"]));
        (hr.WebTimeTrackingAllowed, hr.PasswordChangeRequired, hr.ReceiptName).ShouldBe((true, true, "Neu"));

        // Nobody else without a privilege: forbidden; anonymous: unauthorized.
        (await api.SendAsync(HttpMethod.Get, url, Erika)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await api.SendAsync(HttpMethod.Get, url, null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // A manager with the plain read privilege sees the organisation data, nothing private.
        api.Grant(Max, new TestGrant(EmployeePrivileges.Read, [StoreA], [created.EmployeeId]));
        var manager = await api.GetAsync<EmployeeCardDto>(url, Max);
        (manager.FirstName, manager.Private, manager.Wage).ShouldBe(("Erika", null, null));

        api.Grant(Max, new TestGrant(EmployeePrivileges.Read, [StoreA], [created.EmployeeId]), new TestGrant(EmployeePrivileges.ReadPrivate, [StoreA], [created.EmployeeId]));
        var withPrivate = await api.GetAsync<EmployeeCardDto>(url, Max);
        (withPrivate.Private?.City, withPrivate.Private?.PrivateEmail, withPrivate.Wage).ShouldBe(("Augsburg", "erika@privat.example", null));

        api.Grant(Max, new TestGrant(EmployeePrivileges.Read, [StoreA], [created.EmployeeId]), new TestGrant(EmployeePrivileges.ReadWageType, [StoreA], [created.EmployeeId]));
        var withWage = await api.GetAsync<EmployeeCardDto>(url, Max);
        (withWage.Private, withWage.Wage?.WageTypeId).ShouldBe((null, lists["WageType"]));
    }

    [Fact]
    public async Task The_person_sees_the_own_private_data_but_never_the_wage_type_and_nobody_elses()
    {
        await using var api = await StartAsync(postgres);
        var lists = await SeedListsAsync(api);
        await SetSeriesAsync(api, 10041);
        var created = await CreateAsync(api, Card(lists), Hanna);
        api.Grant(created.EmployeeId, new TestGrant(EmployeePrivileges.ReadOwn));

        var own = await api.GetAsync<EmployeeCardDto>("/api/me/employee-card", created.EmployeeId);
        (own.Private?.Street, own.Private?.PrivatePhone, own.Wage).ShouldBe(("Hauptstr. 1", "+49 821 123456", null));
        var byId = await api.GetAsync<EmployeeCardDto>($"/api/employees/{created.EmployeeId}/card", created.EmployeeId);
        (byId.Private?.City, byId.Wage).ShouldBe(("Augsburg", null));

        // Without the own-privilege nothing; the own privilege does not open anybody else's card.
        (await api.SendAsync(HttpMethod.Get, "/api/me/employee-card", Erika)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        api.Grant(Erika, new TestGrant(EmployeePrivileges.ReadOwn));
        (await api.SendAsync(HttpMethod.Get, $"/api/employees/{created.EmployeeId}/card", Erika)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ---------------------------------------------------------------- changing

    [Fact]
    public async Task HR_changes_the_card_but_the_wage_type_only_with_its_privilege()
    {
        await using var api = await StartAsync(postgres);
        var lists = await SeedListsAsync(api);
        await SetSeriesAsync(api, 10041);
        var created = await CreateAsync(api, Card(lists), Hanna);
        var url = $"/api/employees/{created.EmployeeId}/card";
        api.Grant(Max, new TestGrant(EmployeePrivileges.Update, [StoreA], [created.EmployeeId]));

        (await api.SendAsync(HttpMethod.Put, url, Erika, Card(lists))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await api.SendAsync(HttpMethod.Put, url, null, Card(lists))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // The manager may change the city, but the same request that also changes the wage type is refused.
        (await api.SendAsync(HttpMethod.Put, url, Max, Card(lists, c => c["city"] = "Ulm"))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var other = await api.SendAsync(HttpMethod.Post, "/api/employee-lists/WageType", Hanna, new { code = "L2", name = "Monatslohn" });
        var otherId = (await other.Content.ReadFromJsonAsync<ReferenceEntryDto>())!.Id;
        (await api.SendAsync(HttpMethod.Put, url, Max, Card(lists, c => c["wageTypeId"] = otherId))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await api.SendAsync(HttpMethod.Put, url, Hanna, Card(lists, c => { c["wageTypeId"] = otherId; c["exitDate"] = "2027-06-30"; }))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var card = await api.GetAsync<EmployeeCardDto>(url, Hanna);
        (card.PersonnelNumber, card.Wage?.WageTypeId, card.ExitDate, card.Private?.City).ShouldBe(("10042", otherId, new DateOnly(2027, 6, 30), "Augsburg"));
        (await api.SendAsync(HttpMethod.Put, $"/api/employees/{Guid.NewGuid()}/card", Hanna, Card(lists))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ---------------------------------------------------------------- selection lists and number series

    [Fact]
    public async Task Only_holders_of_the_list_privilege_maintain_the_lists_and_codes_are_unique()
    {
        await using var api = await StartAsync(postgres);

        foreach (var person in new Guid?[] { Max, Erika, null })
        {
            var expected = person is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
            (await api.SendAsync(HttpMethod.Get, "/api/employee-lists/Function", person)).StatusCode.ShouldBe(expected);
            (await api.SendAsync(HttpMethod.Post, "/api/employee-lists/Function", person, new { code = "X", name = "Y" })).StatusCode.ShouldBe(expected);
            (await api.SendAsync(HttpMethod.Put, $"/api/employee-lists/Function/{Guid.NewGuid()}", person, new { name = "Y", isActive = true })).StatusCode.ShouldBe(expected);
        }

        using var created = await api.SendAsync(HttpMethod.Post, "/api/employee-lists/function", Hanna, new { code = "KASSE", name = "Kassierer" });
        created.StatusCode.ShouldBe(HttpStatusCode.OK);
        var entry = (await created.Content.ReadFromJsonAsync<ReferenceEntryDto>())!;
        (await api.SendAsync(HttpMethod.Post, "/api/employee-lists/Function", Hanna, new { code = "kasse", name = "Doppelt" })).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await api.SendAsync(HttpMethod.Post, "/api/employee-lists/Function", Hanna, new { code = "", name = "Y" })).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await api.SendAsync(HttpMethod.Get, "/api/employee-lists/Nonsense", Hanna)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Creators may read the lists to choose from; an entry that was deactivated stays in the list but cannot be chosen any more.
        api.Grant(Max, new TestGrant(EmployeePrivileges.Create, [StoreA], [Max]));
        (await api.GetAsync<List<ReferenceEntryDto>>("/api/employee-lists/Function", Max)).ShouldHaveSingleItem().Code.ShouldBe("KASSE");
        (await api.SendAsync(HttpMethod.Put, $"/api/employee-lists/Function/{entry.Id}", Hanna, new { name = "Kassiererin", isActive = false })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var listed = (await api.GetAsync<List<ReferenceEntryDto>>("/api/employee-lists/Function", Hanna)).Single();
        (listed.Name, listed.IsActive).ShouldBe(("Kassiererin", false));
        await SetSeriesAsync(api, 10041);
        (await api.SendAsync(HttpMethod.Post, "/api/employees", Hanna, new Dictionary<string, object?>
        {
            ["firstName"] = "A", ["lastName"] = "B", ["gender"] = "Male", ["mainStoreId"] = StoreA, ["functionId"] = entry.Id, ["hireDate"] = "2026-12-01", ["qtradoExport"] = false,
        })).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Only_holders_of_the_series_privilege_read_and_set_the_number_series()
    {
        await using var api = await StartAsync(postgres);

        foreach (var person in new Guid?[] { Max, Erika, null })
        {
            var expected = person is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
            (await api.SendAsync(HttpMethod.Get, "/api/personnel-number-series", person)).StatusCode.ShouldBe(expected);
            (await api.SendAsync(HttpMethod.Put, "/api/personnel-number-series", person, new { lastNumber = 5 })).StatusCode.ShouldBe(expected);
        }

        (await api.SendAsync(HttpMethod.Put, "/api/personnel-number-series", Hanna, new { lastNumber = -1 })).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    // ---------------------------------------------------------------- erasure

    [Fact]
    public async Task Erasure_removes_the_card_and_the_traces_of_the_person()
    {
        await using var api = await StartAsync(postgres);
        var lists = await SeedListsAsync(api);
        await SetSeriesAsync(api, 10041);
        var boss = await CreateAsync(api, Card(lists, c => c["firstName"] = "Chef"), Hanna);
        var created = await CreateAsync(api, Card(lists, c => c["supervisorPersonnelNumber"] = boss.PersonnelNumber), Hanna);

        await api.DeliverAsync(new EmployeeDataErasureRequested(Guid.NewGuid(), Guid.NewGuid(), Start, boss.EmployeeId));

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkforceDbContext>();
        (await db.EmployeeCards.AnyAsync(c => c.EmployeeId == boss.EmployeeId)).ShouldBeFalse();
        (await db.People.AnyAsync(p => p.EmployeeId == boss.EmployeeId)).ShouldBeFalse();
        (await db.EmployeeCards.AsNoTracking().SingleAsync(c => c.EmployeeId == created.EmployeeId)).SupervisorId.ShouldBeNull(); // the supervisor reference is gone
        (await db.EmployeeCards.AsNoTracking().SingleAsync(c => c.EmployeeId == created.EmployeeId)).CreatedBy.ShouldBe(Hanna);
        (await db.PersonnelNumberSeries.AsNoTracking().SingleAsync()).ChangedBy.ShouldBe(Hanna);
        (await db.Entitlements.AnyAsync(e => e.EmployeeId == boss.EmployeeId)).ShouldBeFalse();
    }
}
