using System.Net;
using System.Net.Http.Json;
using BcIntegration.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Infrastructure.Messaging;
using Shared.Messaging;
using Shared.Testing;
using Workforce.Api;
using Workforce.Core.Employees;
using Workforce.Infrastructure;
using static Workforce.Api.Tests.WorkforceApi;

namespace Workforce.Api.Tests;

/// <summary>The working time account on the real API. Clock: Friday 2026-10-23.</summary>
public sealed class WorkingTimeEndpointTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly string[] BookRoutes = ["opening-balance", "corrections", "time-off", "payouts"];

    /// <summary>Erika gets a card with a full-time employment relationship (monthly threshold 160 hours).</summary>
    private static async Task<Guid> SeedRelationshipAsync(WorkforceApi api, decimal? threshold = 160)
    {
        var relationshipId = Guid.NewGuid();
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorkforceDbContext>();
            db.ReferenceEntries.Add(new ReferenceEntry { Id = relationshipId, Kind = ReferenceKind.EmploymentRelationship, Code = "VZ", Name = "Vollzeit" });
            db.EmployeeCards.Add(new EmployeeCard
            {
                EmployeeId = Erika, PersonnelNumber = "1001", FirstName = "Erika", LastName = "Mustermann", ReceiptName = "Mustermann", HireDate = new DateOnly(2020, 1, 1),
                EmploymentRelationshipId = relationshipId,
            });
            await db.SaveChangesAsync();
        }

        if (threshold is { } hours)
        {
            (await api.SendAsync(HttpMethod.Put, $"/api/working-time-thresholds/{relationshipId}", Hanna, new SetThresholdRequest(hours))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        return relationshipId;
    }

    private static Task<HttpResponseMessage> Post(WorkforceApi api, string path, Guid? by, object body, Guid? employee = null) =>
        api.SendAsync(HttpMethod.Post, $"/api/employees/{employee ?? Erika}/working-time/{path}", by, body);

    private static async Task<WorkingTimeDto> ViewAsync(WorkforceApi api, Guid by) => await api.GetAsync<WorkingTimeDto>($"/api/employees/{Erika}/working-time", by);

    // ---------------------------------------------------------------- who sees and books

    [Fact]
    public async Task The_account_is_seen_by_the_person_and_holders_of_the_read_privilege_only()
    {
        await using var api = await StartAsync(postgres);

        (await api.GetAsync<WorkingTimeDto>("/api/me/working-time", Erika)).Balance.ShouldBe(0m);
        (await ViewAsync(api, Hanna)).Balance.ShouldBe(0m);

        foreach (var person in new Guid?[] { Ben, Max, Lena, null })
        {
            (await api.SendAsync(HttpMethod.Get, $"/api/employees/{Erika}/working-time", person)).StatusCode.ShouldBe(person is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden);
        }

        (await api.SendAsync(HttpMethod.Get, "/api/me/working-time", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await api.SendAsync(HttpMethod.Get, $"/api/employees/{Guid.NewGuid()}/working-time", Hanna)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Only_HR_books_and_the_reach_of_the_privilege_counts()
    {
        await using var api = await StartAsync(postgres);
        var body = new BookHoursRequest(5, new DateOnly(2026, 10, 1));

        foreach (var route in BookRoutes.Append("payout-at-exit").Append("month-closes"))
        {
            foreach (var person in new Guid?[] { Erika, Ben, Max, null })
            {
                (await Post(api, route, person, body)).StatusCode.ShouldBe(person is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, route);
            }
        }

        // Max may book for his own store only.
        api.Grant(Max, new TestGrant(Workforce.Core.WorkingTime.WorkingTimePrivileges.Book, [StoreA], [Erika, Ben, Max]));
        (await Post(api, "corrections", Max, body)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Post(api, "corrections", Max, body, Lena)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ViewAsync(api, Hanna)).Balance.ShouldBe(5m);
    }

    // ---------------------------------------------------------------- the flow

    [Fact]
    public async Task Close_a_month_book_time_off_and_payout_and_the_balance_follows()
    {
        await using var api = await StartAsync(postgres);
        await SeedRelationshipAsync(api);

        // September: 172 hours worked, threshold 160 -> 12 hours overtime, 1:1.
        using var closed = await Post(api, "month-closes", Hanna, new CloseMonthRequest(2026, 9, 172));
        closed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = (await closed.Content.ReadFromJsonAsync<MonthCloseResultDto>())!;
        (result.Close.OvertimeHours, result.Close.ThresholdHours, result.Booking!.Hours, result.Booking.Date).ShouldBe((12m, 160m, 12m, new DateOnly(2026, 9, 30)));

        (await Post(api, "time-off", Hanna, new BookHoursRequest(4, new DateOnly(2026, 10, 23)))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Post(api, "payouts", Hanna, new BookHoursRequest(3, new DateOnly(2026, 10, 23)))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Post(api, "corrections", Hanna, new BookHoursRequest(-0.5m, new DateOnly(2026, 10, 22)))).StatusCode.ShouldBe(HttpStatusCode.OK);

        var view = await ViewAsync(api, Hanna);
        view.Balance.ShouldBe(4.5m);
        view.Bookings.Select(b => (b.Kind, b.Hours)).ShouldBe([("TimeOff", -4m), ("Payout", -3m), ("Correction", -0.5m), ("Overtime", 12m)], ignoreOrder: true);
        view.Closes.Single().WorkedHours.ShouldBe(172m);
        (await api.GetAsync<WorkingTimeDto>("/api/me/working-time", Erika)).Balance.ShouldBe(4.5m); // the person sees the same
    }

    [Fact]
    public async Task Business_rules_answer_with_clear_statuses()
    {
        await using var api = await StartAsync(postgres);
        await SeedRelationshipAsync(api);
        var today = new DateOnly(2026, 10, 23);

        (await Post(api, "month-closes", Hanna, new CloseMonthRequest(2026, 10, 170))).StatusCode.ShouldBe(HttpStatusCode.Conflict); // October is not over
        (await Post(api, "month-closes", Hanna, new CloseMonthRequest(2026, 13, 170))).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await Post(api, "month-closes", Hanna, new CloseMonthRequest(2026, 9, 170))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Post(api, "month-closes", Hanna, new CloseMonthRequest(2026, 9, 175))).StatusCode.ShouldBe(HttpStatusCode.Conflict); // already closed
        (await Post(api, "time-off", Hanna, new BookHoursRequest(11, today))).StatusCode.ShouldBe(HttpStatusCode.Conflict); // only 10 hours
        (await Post(api, "time-off", Hanna, new BookHoursRequest(0, today))).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await Post(api, "corrections", Hanna, new BookHoursRequest(1, today.AddDays(1)))).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity); // future
        (await Post(api, "opening-balance", Hanna, new BookHoursRequest(2, new DateOnly(2026, 1, 1)))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Post(api, "opening-balance", Hanna, new BookHoursRequest(2, new DateOnly(2026, 1, 1)))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Post(api, "payout-at-exit", Hanna, new { })).StatusCode.ShouldBe(HttpStatusCode.Conflict); // not leaving
        (await Post(api, "corrections", Hanna, new BookHoursRequest(1, today), Guid.NewGuid())).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Without_a_threshold_or_relationship_a_month_cannot_be_closed()
    {
        await using var api = await StartAsync(postgres);

        (await Post(api, "month-closes", Hanna, new CloseMonthRequest(2026, 9, 170))).StatusCode.ShouldBe(HttpStatusCode.Conflict); // no card, no relationship
        await SeedRelationshipAsync(api, threshold: null);
        (await Post(api, "month-closes", Hanna, new CloseMonthRequest(2026, 9, 170))).StatusCode.ShouldBe(HttpStatusCode.Conflict); // no threshold set
    }

    [Fact]
    public async Task At_exit_the_whole_balance_is_paid_out()
    {
        await using var api = await StartAsync(postgres);
        await Post(api, "corrections", Hanna, new BookHoursRequest(7.5m, new DateOnly(2026, 10, 1)));
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorkforceDbContext>();
            (await db.People.SingleAsync(p => p.EmployeeId == Erika)).ExitDate = new DateOnly(2026, 11, 30);
            await db.SaveChangesAsync();
        }

        using var paid = await Post(api, "payout-at-exit", Hanna, new { });

        paid.StatusCode.ShouldBe(HttpStatusCode.OK);
        var booking = (await paid.Content.ReadFromJsonAsync<TimeBookingDto>())!;
        (booking.Kind, booking.Hours, booking.Date).ShouldBe(("PayoutAtExit", -7.5m, new DateOnly(2026, 11, 30)));
        (await Post(api, "payout-at-exit", Hanna, new { })).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    // ---------------------------------------------------------------- the rules

    [Fact]
    public async Task With_minus_hours_switched_on_by_HR_a_month_below_the_threshold_books_minus_hours()
    {
        await using var api = await StartAsync(postgres);
        await SeedRelationshipAsync(api);
        var defaults = await api.GetAsync<WorkingTimeRulesDto>("/api/working-time-rules", Hanna);
        defaults.BookMinusHours.ShouldBeFalse();

        // Off by default: 150 hours against the threshold books nothing.
        using (var off = await Post(api, "month-closes", Hanna, new CloseMonthRequest(2026, 8, 150)))
        {
            off.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await off.Content.ReadFromJsonAsync<MonthCloseResultDto>())!.Booking.ShouldBeNull();
        }

        (await api.SendAsync(HttpMethod.Put, "/api/working-time-rules", Hanna, new SetWorkingTimeRulesRequest(0, null, true))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await api.GetAsync<WorkingTimeRulesDto>("/api/working-time-rules", Hanna)).BookMinusHours.ShouldBeTrue();

        using var on = await Post(api, "month-closes", Hanna, new CloseMonthRequest(2026, 9, 150));
        on.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = (await on.Content.ReadFromJsonAsync<MonthCloseResultDto>())!;
        (result.Close.MinusHours, result.Close.OvertimeHours, result.Booking!.Kind, result.Booking.Hours, result.Booking.Date).ShouldBe((10m, 0m, "MinusHours", -10m, new DateOnly(2026, 9, 30)));
        var view = await api.GetAsync<WorkingTimeDto>($"/api/employees/{Erika}/working-time", Hanna);
        (view.Balance, view.Deficit, view.Rules.BookMinusHours).ShouldBe((-10m, 10m, true));
    }

    [Fact]
    public async Task A_limit_for_the_minus_hours_set_by_HR_stops_the_booking_and_tells_what_was_left_out()
    {
        await using var api = await StartAsync(postgres);
        await SeedRelationshipAsync(api);
        (await api.SendAsync(HttpMethod.Put, "/api/working-time-rules", Hanna, new SetWorkingTimeRulesRequest(0, null, true, 20))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var rules = await api.GetAsync<WorkingTimeRulesDto>("/api/working-time-rules", Hanna);
        (rules.BookMinusHours, rules.MaxMinusHours).ShouldBe((true, 20m));
        (await api.SendAsync(HttpMethod.Put, "/api/working-time-rules", Hanna, new SetWorkingTimeRulesRequest(0, null, true, -1))).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        using var closed = await Post(api, "month-closes", Hanna, new CloseMonthRequest(2026, 9, 130)); // 30 missing, 20 allowed
        closed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = (await closed.Content.ReadFromJsonAsync<MonthCloseResultDto>())!;

        (result.Close.MinusHours, result.Close.MinusHoursNotBooked, result.Booking!.Hours).ShouldBe((20m, 10m, -20m));
        (await api.GetAsync<WorkingTimeDto>($"/api/employees/{Erika}/working-time", Hanna)).Balance.ShouldBe(-20m);
    }

    [Fact]
    public async Task HR_sets_the_rules_for_everybody_and_they_apply_to_everything_already_booked()
    {
        await using var api = await StartAsync(postgres);
        await Post(api, "corrections", Hanna, new BookHoursRequest(10, new DateOnly(2026, 1, 31)));
        await Post(api, "corrections", Hanna, new BookHoursRequest(4, new DateOnly(2026, 9, 30)));

        var defaults = await api.GetAsync<WorkingTimeRulesDto>("/api/working-time-rules", Hanna);
        (defaults.PayoutThresholdHours, defaults.ExpiryMonths, defaults.IsDefault).ShouldBe((0m, (int?)null, true));
        (await ViewAsync(api, Hanna)).Balance.ShouldBe(14m);

        (await api.SendAsync(HttpMethod.Put, "/api/working-time-rules", Hanna, new SetWorkingTimeRulesRequest(20, 3))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var set = await api.GetAsync<WorkingTimeRulesDto>("/api/working-time-rules", Hanna);
        (set.PayoutThresholdHours, set.ExpiryMonths, set.IsDefault, set.ChangedBy).ShouldBe((20m, (int?)3, false, (Guid?)Hanna));
        var view = await ViewAsync(api, Hanna);
        (view.Balance, view.Forfeited).ShouldBe((4m, 10m)); // January hours expired, the payout threshold 20 now blocks payouts
        (await Post(api, "payouts", Hanna, new BookHoursRequest(1, new DateOnly(2026, 10, 23)))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await api.SendAsync(HttpMethod.Put, "/api/working-time-rules", Hanna, new SetWorkingTimeRulesRequest(-1, null))).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Rules_and_thresholds_are_for_holders_of_the_manage_privilege_only()
    {
        await using var api = await StartAsync(postgres);
        var relationship = await SeedRelationshipAsync(api, threshold: null);

        foreach (var person in new Guid?[] { Erika, Max, null })
        {
            var expected = person is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
            (await api.SendAsync(HttpMethod.Get, "/api/working-time-rules", person)).StatusCode.ShouldBe(expected);
            (await api.SendAsync(HttpMethod.Put, "/api/working-time-rules", person, new SetWorkingTimeRulesRequest(0, null))).StatusCode.ShouldBe(expected);
            (await api.SendAsync(HttpMethod.Get, "/api/working-time-thresholds", person)).StatusCode.ShouldBe(expected);
            (await api.SendAsync(HttpMethod.Put, $"/api/working-time-thresholds/{relationship}", person, new SetThresholdRequest(160))).StatusCode.ShouldBe(expected);
        }

        (await api.SendAsync(HttpMethod.Put, $"/api/working-time-thresholds/{relationship}", Hanna, new SetThresholdRequest(80))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await api.GetAsync<List<MonthlyThresholdDto>>("/api/working-time-thresholds", Hanna)).ShouldBe([new MonthlyThresholdDto(relationship, 80m)]);
        (await api.SendAsync(HttpMethod.Put, $"/api/working-time-thresholds/{relationship}", Hanna, new SetThresholdRequest(null))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await api.GetAsync<List<MonthlyThresholdDto>>("/api/working-time-thresholds", Hanna)).ShouldBeEmpty();
        (await api.SendAsync(HttpMethod.Put, $"/api/working-time-thresholds/{Guid.NewGuid()}", Hanna, new SetThresholdRequest(80))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await api.SendAsync(HttpMethod.Put, $"/api/working-time-thresholds/{relationship}", Hanna, new SetThresholdRequest(745))).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    // ---------------------------------------------------------------- erasure

    [Fact]
    public async Task Erasure_removes_the_account_and_anonymises_the_author_of_bookings()
    {
        await using var api = await StartAsync(postgres);
        await SeedRelationshipAsync(api);
        await Post(api, "month-closes", Hanna, new CloseMonthRequest(2026, 9, 172));
        await Post(api, "corrections", Hanna, new BookHoursRequest(1, new DateOnly(2026, 10, 1)));

        (await api.DeliverAsync(new EmployeeDataErasureRequested(Guid.NewGuid(), Guid.NewGuid(), Start, Erika))).Outcome.ShouldBe(DispatchOutcome.Completed);
        (await api.DeliverAsync(new EmployeeDataErasureRequested(Guid.NewGuid(), Guid.NewGuid(), Start, Hanna))).Outcome.ShouldBe(DispatchOutcome.Completed);

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkforceDbContext>();
        (await db.TimeBookings.AnyAsync()).ShouldBeFalse();
        (await db.MonthCloses.AnyAsync()).ShouldBeFalse();
        (await db.MonthlyThresholds.AllAsync(t => t.ChangedBy == Guid.Empty)).ShouldBeTrue();
    }
}
