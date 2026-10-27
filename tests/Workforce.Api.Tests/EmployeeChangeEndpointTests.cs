using System.Net;
using System.Net.Http.Json;
using Access.Contracts;
using BcIntegration.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Testing;
using Workforce.Api;
using Workforce.Core.Employees;
using Workforce.Infrastructure;
using static Workforce.Api.Tests.WorkforceApi;

namespace Workforce.Api.Tests;

/// <summary>A person can only ask for a change of the own contact data; HR is told and decides. Nothing about the data is in a message.</summary>
public sealed class EmployeeChangeEndpointTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Requests = "/api/me/employee-card/change-requests";

    /// <summary>A new employee (with the right to ask) whose changes HR (Hanna) decides.</summary>
    private static async Task<Guid> NewEmployeeAsync(WorkforceApi api, bool withHandler = true)
    {
        var lists = new Dictionary<string, Guid>();
        foreach (var (kind, code, name) in new[] { ("Function", "KASSE", "Kassierer"), ("WageType", "L1", "Stundenlohn") })
        {
            using var entry = await api.SendAsync(HttpMethod.Post, $"/api/employee-lists/{kind}", Hanna, new { code, name });
            lists[kind] = (await entry.Content.ReadFromJsonAsync<ReferenceEntryDto>())!.Id;
        }

        (await api.SendAsync(HttpMethod.Put, "/api/personnel-number-series", Hanna, new { lastNumber = 15301 })).StatusCode.ShouldBe(HttpStatusCode.OK);
        using var created = await api.SendAsync(HttpMethod.Post, "/api/employees", Hanna, new Dictionary<string, object?>
        {
            ["firstName"] = "Erika", ["lastName"] = "Neu", ["gender"] = "Female", ["street"] = "Hauptstr. 1", ["postalCode"] = "86150", ["city"] = "Augsburg",
            ["privateEmail"] = "erika@privat.example", ["mainStoreId"] = StoreA, ["functionId"] = lists["Function"], ["wageTypeId"] = lists["WageType"],
            ["hireDate"] = "2026-12-01", ["qtradoExport"] = false,
        });
        created.StatusCode.ShouldBe(HttpStatusCode.OK);
        var id = (await created.Content.ReadFromJsonAsync<CreatedEmployeeDto>())!.EmployeeId;
        api.Grant(id, new TestGrant(EmployeePrivileges.ReadOwn), new TestGrant(EmployeePrivileges.RequestChangeOwn));
        if (withHandler)
        {
            await api.DeliverAsync(new ResponsibilitiesChanged(Guid.NewGuid(), Guid.NewGuid(), Start, id, 1, [new TopicResponsibility("employee.change-request", [Hanna])]));
        }

        return id;
    }

    private static object Ask(string? street = "Neue Str. 5", string? city = "Ulm", string? email = null, string? message = null) =>
        new { street, city, privateEmail = email, message };

    // ---------------------------------------------------------------- the whole way

    [Fact]
    public async Task The_person_asks_nothing_changes_HR_decides_and_the_data_follows_only_after_approval()
    {
        await using var api = await StartAsync(postgres);
        var me = await NewEmployeeAsync(api);

        using var asked = await api.SendAsync(HttpMethod.Post, Requests, me, Ask(message: "Umzug zum 1.12."));
        asked.StatusCode.ShouldBe(HttpStatusCode.OK);
        var request = (await asked.Content.ReadFromJsonAsync<EmployeeChangeRequestDto>())!;
        (request.Status, request.Fields.Street, request.Fields.City, request.Fields.PostalCode, request.Message).ShouldBe(("Pending", "Neue Str. 5", "Ulm", null, "Umzug zum 1.12."));

        // The person cannot change the own data: not through the card endpoint, not by approving the own request. The card is still the old one.
        (await api.SendAsync(HttpMethod.Put, $"/api/employees/{me}/card", me, new { firstName = "Erika", lastName = "Neu", gender = "Female", street = "Neue Str. 5", hireDate = "2026-12-01", qtradoExport = false })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await api.GetAsync<EmployeeCardDto>("/api/me/employee-card", me)).Private!.Street.ShouldBe("Hauptstr. 1");

        // HR sees the pending request with the person and the proposed data.
        var pending = await api.GetAsync<List<EmployeeChangeRequestDto>>("/api/employee-change-requests", Hanna);
        var seen = pending.ShouldHaveSingleItem();
        (seen.Id, seen.PersonName, seen.PersonnelNumber, seen.Fields.City).ShouldBe((request.Id, "Erika Neu", "15302", "Ulm"));

        (await api.SendAsync(HttpMethod.Post, $"/api/employee-change-requests/{request.Id}/approve", Hanna)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var card = await api.GetAsync<EmployeeCardDto>($"/api/employees/{me}/card", Hanna);
        (card.Private!.Street, card.Private.City, card.Private.PostalCode, card.Private.PrivateEmail).ShouldBe(("Neue Str. 5", "Ulm", "86150", "erika@privat.example"));
        (await api.GetAsync<EmployeeCardDto>("/api/me/employee-card", me)).Private!.City.ShouldBe("Ulm");
        var own = (await api.GetAsync<List<EmployeeChangeRequestDto>>(Requests, me)).ShouldHaveSingleItem();
        (own.Status, own.PersonName).ShouldBe(("Approved", null));
        (await api.GetAsync<List<EmployeeChangeRequestDto>>("/api/employee-change-requests", Hanna)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_rejection_leaves_the_data_alone_and_shows_the_person_the_reason()
    {
        await using var api = await StartAsync(postgres);
        var me = await NewEmployeeAsync(api);
        using var asked = await api.SendAsync(HttpMethod.Post, Requests, me, Ask());
        var request = (await asked.Content.ReadFromJsonAsync<EmployeeChangeRequestDto>())!;

        (await api.SendAsync(HttpMethod.Post, $"/api/employee-change-requests/{request.Id}/reject", Hanna, new { reason = "Bitte Meldebescheinigung vorlegen" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await api.GetAsync<EmployeeCardDto>("/api/me/employee-card", me)).Private!.City.ShouldBe("Augsburg");
        var own = (await api.GetAsync<List<EmployeeChangeRequestDto>>(Requests, me)).Single();
        (own.Status, own.RejectionReason).ShouldBe(("Rejected", "Bitte Meldebescheinigung vorlegen"));
        // A decided request cannot be decided again; the person may ask anew.
        (await api.SendAsync(HttpMethod.Post, $"/api/employee-change-requests/{request.Id}/approve", Hanna)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await api.SendAsync(HttpMethod.Post, Requests, me, Ask(city: "Neu-Ulm"))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HR_and_the_person_are_told_without_any_of_the_data_in_the_messages()
    {
        await using var api = await StartAsync(postgres);
        var me = await NewEmployeeAsync(api);
        using var asked = await api.SendAsync(HttpMethod.Post, Requests, me, Ask(email: "geheim@privat.example", message: "Streng vertraulich"));
        var request = (await asked.Content.ReadFromJsonAsync<EmployeeChangeRequestDto>())!;
        await api.SendAsync(HttpMethod.Post, $"/api/employee-change-requests/{request.Id}/approve", Hanna);

        var notifications = (await api.OutboxAsync()).Where(m => m.Payload.Contains("employee-change-", StringComparison.Ordinal)).ToList();

        notifications.Count.ShouldBe(2);
        var toHr = notifications.Single(m => m.Payload.Contains("workforce.employee-change-request.hr", StringComparison.Ordinal));
        var toPerson = notifications.Single(m => m.Payload.Contains("workforce.employee-change-decision.employee", StringComparison.Ordinal));
        toHr.Payload.ShouldContain(Hanna.ToString());
        toPerson.Payload.ShouldContain(me.ToString());
        foreach (var message in notifications)
        {
            foreach (var secret in new[] { "Neue Str", "Ulm", "geheim@", "Streng vertraulich", "Hauptstr" })
            {
                message.Payload.ShouldNotContain(secret);
            }
        }
    }

    // ---------------------------------------------------------------- who may do what

    [Fact]
    public async Task Everything_needs_a_signed_in_person_with_the_right_privilege()
    {
        await using var api = await StartAsync(postgres);
        var me = await NewEmployeeAsync(api);
        using var asked = await api.SendAsync(HttpMethod.Post, Requests, me, Ask());
        var request = (await asked.Content.ReadFromJsonAsync<EmployeeChangeRequestDto>())!;

        foreach (var (method, url) in new[]
        {
            (HttpMethod.Post, Requests), (HttpMethod.Get, Requests), (HttpMethod.Delete, $"{Requests}/{request.Id}"),
            (HttpMethod.Get, "/api/employee-change-requests"), (HttpMethod.Post, $"/api/employee-change-requests/{request.Id}/approve"), (HttpMethod.Post, $"/api/employee-change-requests/{request.Id}/reject"),
        })
        {
            (await api.SendAsync(method, url, null, method == HttpMethod.Post ? Ask() : null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized, $"{method} {url}");
        }

        // Somebody without the own-privilege cannot ask or read the own requests; somebody without the decide privilege cannot decide.
        (await api.SendAsync(HttpMethod.Post, Requests, Erika, Ask())).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await api.SendAsync(HttpMethod.Get, Requests, Erika)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await api.SendAsync(HttpMethod.Post, $"/api/employee-change-requests/{request.Id}/approve", Max)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await api.SendAsync(HttpMethod.Post, $"/api/employee-change-requests/{request.Id}/reject", Max)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await api.GetAsync<List<EmployeeChangeRequestDto>>("/api/employee-change-requests", Max)).ShouldBeEmpty(); // sees nothing, not even that there is one
        (await api.SendAsync(HttpMethod.Post, $"/api/employee-change-requests/{Guid.NewGuid()}/approve", Hanna)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Whoever_decides_only_decides_for_the_persons_in_reach_and_never_the_own_request()
    {
        await using var api = await StartAsync(postgres);
        var me = await NewEmployeeAsync(api);
        using var asked = await api.SendAsync(HttpMethod.Post, Requests, me, Ask());
        var request = (await asked.Content.ReadFromJsonAsync<EmployeeChangeRequestDto>())!;

        api.Grant(Lena, new TestGrant(EmployeePrivileges.DecideChange, [StoreB], [Lena]));
        (await api.SendAsync(HttpMethod.Post, $"/api/employee-change-requests/{request.Id}/approve", Lena)).StatusCode.ShouldBe(HttpStatusCode.Forbidden); // another store

        // Even with the decide privilege for oneself: nobody decides the own request.
        api.Grant(me, new TestGrant(EmployeePrivileges.ReadOwn), new TestGrant(EmployeePrivileges.RequestChangeOwn), new TestGrant(EmployeePrivileges.DecideChange, [StoreA], [me]));
        (await api.SendAsync(HttpMethod.Post, $"/api/employee-change-requests/{request.Id}/approve", me)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await api.GetAsync<EmployeeCardDto>("/api/me/employee-card", me)).Private!.City.ShouldBe("Augsburg");

        api.Grant(Max, new TestGrant(EmployeePrivileges.DecideChange, [StoreA], [me]));
        (await api.GetAsync<List<EmployeeChangeRequestDto>>("/api/employee-change-requests", Max)).ShouldHaveSingleItem();
        (await api.SendAsync(HttpMethod.Post, $"/api/employee-change-requests/{request.Id}/approve", Max)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    // ---------------------------------------------------------------- the rules of a request

    [Fact]
    public async Task One_pending_request_at_a_time_and_the_person_can_withdraw_only_the_own()
    {
        await using var api = await StartAsync(postgres);
        var me = await NewEmployeeAsync(api);
        using var asked = await api.SendAsync(HttpMethod.Post, Requests, me, Ask());
        var request = (await asked.Content.ReadFromJsonAsync<EmployeeChangeRequestDto>())!;

        (await api.SendAsync(HttpMethod.Post, Requests, me, Ask(city: "Neu-Ulm"))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        api.Grant(Erika, new TestGrant(EmployeePrivileges.RequestChangeOwn));
        (await api.SendAsync(HttpMethod.Delete, $"{Requests}/{request.Id}", Erika)).StatusCode.ShouldBe(HttpStatusCode.NotFound); // somebody else's request looks missing

        (await api.SendAsync(HttpMethod.Delete, $"{Requests}/{request.Id}", me)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await api.SendAsync(HttpMethod.Delete, $"{Requests}/{request.Id}", me)).StatusCode.ShouldBe(HttpStatusCode.Conflict); // already withdrawn
        (await api.GetAsync<List<EmployeeChangeRequestDto>>("/api/employee-change-requests", Hanna)).ShouldBeEmpty();
        (await api.SendAsync(HttpMethod.Post, Requests, me, Ask(city: "Neu-Ulm"))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_request_needs_somebody_in_HR_to_decide_it_and_something_sensible_to_say()
    {
        await using var api = await StartAsync(postgres);
        var me = await NewEmployeeAsync(api, withHandler: false);

        (await api.SendAsync(HttpMethod.Post, Requests, me, Ask())).StatusCode.ShouldBe(HttpStatusCode.Conflict); // nobody responsible

        await api.DeliverAsync(new ResponsibilitiesChanged(Guid.NewGuid(), Guid.NewGuid(), Start, me, 1, [new TopicResponsibility("employee.change-request", [Hanna])]));
        foreach (var invalid in new[] { Ask(null, null), Ask(email: "not-an-email"), Ask("Hauptstr. 1", "Augsburg") })
        {
            (await api.SendAsync(HttpMethod.Post, Requests, me, invalid)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        }

        (await api.SendAsync(HttpMethod.Post, Requests, me, Ask(null, null, message: "Neuer Nachname nach der Heirat"))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---------------------------------------------------------------- erasure

    [Fact]
    public async Task Erasure_removes_the_requests_of_the_person()
    {
        await using var api = await StartAsync(postgres);
        var me = await NewEmployeeAsync(api);
        (await api.SendAsync(HttpMethod.Post, Requests, me, Ask())).StatusCode.ShouldBe(HttpStatusCode.OK);

        await api.DeliverAsync(new EmployeeDataErasureRequested(Guid.NewGuid(), Guid.NewGuid(), Start, me));

        using var scope = api.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<WorkforceDbContext>().EmployeeChangeRequests.AnyAsync(r => r.EmployeeId == me)).ShouldBeFalse();
    }
}
