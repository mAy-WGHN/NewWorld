using System.Net.Http.Json;
using System.Net;
using Access.Contracts;
using BcIntegration.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Messaging;
using Shared.Testing;
using Workforce.Api;
using Workforce.Core.Absences;
using Workforce.Infrastructure;
using static Workforce.Api.Tests.WorkforceApi;

namespace Workforce.Api.Tests;

public sealed class AbsenceEndpointTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly ReportSickRequest SickToday = new(new DateOnly(2026, 10, 23), new DateOnly(2026, 10, 25));

    [Fact]
    public async Task Many_stores_can_be_marked_open_on_holidays_at_once_all_or_none()
    {
        await using var api = await StartAsync(postgres);
        api.Grant(Max, new TestGrant(AbsencePrivileges.ManageHolidayOpening, [StoreA], [Erika, Ben, Max]));
        var both = new { storeIds = new[] { StoreA, StoreB }, opensOnHolidays = true };

        // Max reaches only store A: the whole request is refused and nothing changes.
        (await api.SendAsync(HttpMethod.Put, "/api/stores/holiday-opening", Max, both)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await api.GetAsync<StoreHolidayOpeningDto>($"/api/stores/{StoreA}/holiday-opening", Hanna)).OpensOnHolidays.ShouldBeFalse();
        (await api.SendAsync(HttpMethod.Put, "/api/stores/holiday-opening", Erika, both)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await api.SendAsync(HttpMethod.Put, "/api/stores/holiday-opening", null, both)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await api.SendAsync(HttpMethod.Put, "/api/stores/holiday-opening", Hanna, new { storeIds = Array.Empty<Guid>(), opensOnHolidays = true })).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await api.SendAsync(HttpMethod.Put, "/api/stores/holiday-opening", Hanna, new { storeIds = new[] { StoreA, Guid.NewGuid() }, opensOnHolidays = true })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await api.GetAsync<StoreHolidayOpeningDto>($"/api/stores/{StoreA}/holiday-opening", Hanna)).OpensOnHolidays.ShouldBeFalse();

        // Max may set his own store through the same action; HR sets both.
        (await api.SendAsync(HttpMethod.Put, "/api/stores/holiday-opening", Max, new { storeIds = new[] { StoreA }, opensOnHolidays = true })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await api.SendAsync(HttpMethod.Put, "/api/stores/holiday-opening", Hanna, both)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await api.GetAsync<StoreHolidayOpeningDto>($"/api/stores/{StoreA}/holiday-opening", Hanna)).OpensOnHolidays.ShouldBeTrue();
        (await api.GetAsync<StoreHolidayOpeningDto>($"/api/stores/{StoreB}/holiday-opening", Hanna)).OpensOnHolidays.ShouldBeTrue();
        (await api.SendAsync(HttpMethod.Put, "/api/stores/holiday-opening", Hanna, new { storeIds = new[] { StoreA, StoreB }, opensOnHolidays = false })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await api.GetAsync<StoreHolidayOpeningDto>($"/api/stores/{StoreB}/holiday-opening", Hanna)).OpensOnHolidays.ShouldBeFalse();
    }

    [Fact]
    public async Task Additional_vacation_days_are_maintained_by_HR_and_show_up_in_the_balance()
    {
        await using var api = await StartAsync(postgres);
        var url = $"/api/employees/{Erika}/vacation-extra-days";
        (await api.SendAsync(HttpMethod.Put, $"/api/employees/{Erika}/vacation-entitlements/2026", Hanna, new SetEntitlementRequest(2))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Only holders of the privilege (HR) may read or change them; the store manager and the person themself may not.
        foreach (var person in new Guid?[] { Max, Erika, null })
        {
            var expected = person is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
            (await api.SendAsync(HttpMethod.Get, url, person)).StatusCode.ShouldBe(expected);
            (await api.SendAsync(HttpMethod.Post, url, person, new { kind = "Additional", year = 2026, days = 3 })).StatusCode.ShouldBe(expected);
            (await api.SendAsync(HttpMethod.Delete, $"{url}/{Guid.NewGuid()}", person)).StatusCode.ShouldBe(expected);
        }

        foreach (var invalid in new object[] { new { kind = "Nonsense", year = 2026, days = 3 }, new { kind = "Additional", year = 2026, days = 0 }, new { kind = "Additional", year = 2026, days = 2.25 } })
        {
            (await api.SendAsync(HttpMethod.Post, url, Hanna, invalid)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        }

        using var created = await api.SendAsync(HttpMethod.Post, url, Hanna, new { kind = "Additional", year = 2026, days = 3 });
        created.StatusCode.ShouldBe(HttpStatusCode.OK);
        var extra = (await created.Content.ReadFromJsonAsync<ExtraVacationDto>())!;
        (extra.Kind, extra.Year, extra.Days, extra.UsableFrom, extra.ExpiresOn).ShouldBe(("Additional", 2026, 3m, new DateOnly(2026, 1, 1), new DateOnly(2027, 3, 31)));
        (await api.GetAsync<List<ExtraVacationDto>>(url, Hanna)).ShouldHaveSingleItem().Id.ShouldBe(extra.Id);

        // Effective days = contract 2 + additional 3; the person sees the same account.
        var balance = await api.GetAsync<VacationBalanceDto>("/api/me/vacation-balance?year=2026", Erika);
        (balance.Entitlement, balance.ContractualAnnualDays, balance.ContractualDays, balance.ExtraDays, balance.Remaining).ShouldBe((5m, 2m, 2m, 3m, 5m));
        balance.ExtraLines.ShouldNotBeNull().ShouldHaveSingleItem().Kind.ShouldBe("Additional");

        (await api.SendAsync(HttpMethod.Delete, $"{url}/{extra.Id}", Hanna)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await api.SendAsync(HttpMethod.Delete, $"{url}/{extra.Id}", Hanna)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await api.GetAsync<VacationBalanceDto>("/api/me/vacation-balance?year=2026", Erika)).Entitlement.ShouldBe(2m);
    }

    [Fact]
    public async Task The_year_of_joining_is_pro_rata_from_the_entry_date_in_bc()
    {
        await using var api = await StartAsync(postgres);
        await api.DeliverAsync(new BcIntegration.Contracts.EmployeeChanged(
            Guid.NewGuid(), Guid.NewGuid(), Start, Erika, "10042", "Erika Mustermann", StoreA, null, null, null, new DateOnly(2026, 10, 1), null));
        (await api.SendAsync(HttpMethod.Put, $"/api/employees/{Erika}/vacation-entitlements/2026", Hanna, new SetEntitlementRequest(25))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // From 1 October: 3 full months, 25 / 12 * 3 = 6.25, rounded up to 6.5 days.
        var balance = await api.GetAsync<VacationBalanceDto>("/api/me/vacation-balance?year=2026", Erika);
        (balance.ContractualAnnualDays, balance.ContractualDays, balance.Entitlement).ShouldBe((25m, 6.5m, 6.5m));
    }

    [Fact]
    public async Task A_store_open_on_holidays_makes_holidays_working_days_and_only_the_responsible_manager_may_set_it()
    {
        await using var api = await StartAsync(postgres);
        await api.SetEntitlementAsync(Erika, 2026, 30);
        var url = $"/api/stores/{StoreA}/holiday-opening";
        var christmas = new RequestVacationRequest(new DateOnly(2026, 12, 25), new DateOnly(2026, 12, 25)); // Friday, public holiday
        api.Grant(Max, new TestGrant(AbsencePrivileges.ManageHolidayOpening, [StoreA], [Erika, Ben, Max]));

        // Closed on holidays by default: a vacation request on a holiday has no working day.
        (await api.GetAsync<StoreHolidayOpeningDto>(url, Hanna)).OpensOnHolidays.ShouldBeFalse();
        using var closed = await api.SendAsync(HttpMethod.Post, "/api/me/absences/vacation", Erika, christmas);
        closed.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        // Without the privilege, or without reach over the store, nothing can be read or changed.
        foreach (var person in new Guid?[] { Erika, Ben, Lena, null })
        {
            var expected = person is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
            (await api.SendAsync(HttpMethod.Get, url, person)).StatusCode.ShouldBe(expected);
            (await api.SendAsync(HttpMethod.Put, url, person, new { opensOnHolidays = true })).StatusCode.ShouldBe(expected);
        }

        (await api.SendAsync(HttpMethod.Put, $"/api/stores/{StoreB}/holiday-opening", Max, new { opensOnHolidays = true })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await api.SendAsync(HttpMethod.Put, $"/api/stores/{Guid.NewGuid()}/holiday-opening", Hanna, new { opensOnHolidays = true })).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The manager of the store marks it as open on holidays: the same request now counts a working day.
        (await api.SendAsync(HttpMethod.Put, url, Max, new { opensOnHolidays = true })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var set = await api.GetAsync<StoreHolidayOpeningDto>(url, Max);
        (set.OpensOnHolidays, set.ChangedBy).ShouldBe((true, (Guid?)Max));
        (await api.PostAbsenceAsync("/api/me/absences/vacation", Erika, christmas)).Status.ShouldBe("Requested");
        // A colleague of the other store is not affected.
        (await api.GetAsync<StoreHolidayOpeningDto>($"/api/stores/{StoreB}/holiday-opening", Hanna)).OpensOnHolidays.ShouldBeFalse();

        (await api.SendAsync(HttpMethod.Put, url, Hanna, new { opensOnHolidays = false })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await api.GetAsync<StoreHolidayOpeningDto>(url, Hanna)).OpensOnHolidays.ShouldBeFalse();
    }

    [Fact]
    public async Task Sick_note_writes_exactly_three_outbox_messages_and_the_reason_only_goes_to_bc()
    {
        await using var api = await StartAsync(postgres);

        var absence = await api.PostAbsenceAsync("/api/me/absences/sick", Erika, SickToday);

        absence.Type.ShouldBe("Sickness");
        var outbox = await api.OutboxAsync();
        outbox.Select(m => m.MessageType).Order().ShouldBe(["EmployeeAvailabilityChanged", "SendNotification", "SyncAbsenceToBc"]);
        foreach (var message in outbox.Where(m => m.MessageType != "SyncAbsenceToBc"))
        {
            message.Payload.ShouldNotContain("Sickness", Case.Insensitive, $"{message.MessageType} must not carry the reason");
            message.Payload.ShouldNotContain("Krank", Case.Insensitive);
        }

        outbox.Single(m => m.MessageType == "SyncAbsenceToBc").Payload.ShouldContain("Sickness");
        var notification = outbox.Single(m => m.MessageType == "SendNotification").Payload;
        notification.ShouldContain("InApp");
        notification.ShouldNotContain("Push");
        notification.ShouldNotContain("Email");
    }

    [Fact]
    public async Task Colleague_sees_absent_without_reason_while_manager_and_hr_see_the_reason()
    {
        await using var api = await StartAsync(postgres);
        await api.PostAbsenceAsync("/api/me/absences/sick", Erika, SickToday);

        var url = $"/api/stores/{StoreA}/absences?from=2026-10-23&to=2026-10-31";
        var colleague = (await api.GetAsync<List<StoreAbsenceDto>>(url, Ben)).Single();
        var manager = (await api.GetAsync<List<StoreAbsenceDto>>(url, Max)).Single();

        colleague.EmployeeName.ShouldBe("Erika Mustermann");
        colleague.Type.ShouldBeNull();
        colleague.Status.ShouldBe("Absent");
        manager.Type.ShouldBe("Sickness");
        manager.Status.ShouldBe("Reported");
    }

    [Fact]
    public async Task Colleague_cannot_read_the_reason_through_the_employee_endpoints()
    {
        await using var api = await StartAsync(postgres);
        await api.PostAbsenceAsync("/api/me/absences/sick", Erika, SickToday);

        using var absences = await api.SendAsync(HttpMethod.Get, $"/api/employees/{Erika}/absences", Ben);
        using var balance = await api.SendAsync(HttpMethod.Get, $"/api/employees/{Erika}/vacation-balance", Ben);

        absences.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        balance.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Manager_of_store_A_sees_neither_store_B_nor_its_employees()
    {
        await using var api = await StartAsync(postgres);
        await api.PostAbsenceAsync("/api/me/absences/sick", Lena, SickToday);

        using var store = await api.SendAsync(HttpMethod.Get, $"/api/stores/{StoreB}/absences", Max);
        using var employee = await api.SendAsync(HttpMethod.Get, $"/api/employees/{Lena}/absences", Max);
        using var sick = await api.SendAsync(HttpMethod.Post, $"/api/employees/{Lena}/absences/sick", Max, SickToday);

        store.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        employee.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        sick.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Substitute_sees_store_B_only_while_the_substitution_grants_it()
    {
        await using var api = await StartAsync(postgres);
        await api.PostAbsenceAsync("/api/me/absences/sick", Lena, SickToday);
        var url = $"/api/stores/{StoreB}/absences?from=2026-10-23&to=2026-10-31";

        using (var before = await api.SendAsync(HttpMethod.Get, url, Sven))
        {
            before.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        // PermissionsChanged at the start of the substitution (Access decides by time, the copy carries the result) ...
        api.Grant(Sven, new TestGrant(AbsencePrivileges.ReadReason, [StoreB], [Lena]));
        (await api.GetAsync<List<StoreAbsenceDto>>(url, Sven)).Single().Type.ShouldBe("Sickness");

        // ... and at its end.
        api.Grant(Sven);
        using var after = await api.SendAsync(HttpMethod.Get, url, Sven);
        after.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Manager_can_report_a_sick_note_for_an_employee_of_the_own_store()
    {
        await using var api = await StartAsync(postgres);

        var absence = await api.PostAbsenceAsync($"/api/employees/{Erika}/absences/sick", Max, SickToday);

        absence.EmployeeId.ShouldBe(Erika);
        (await api.GetAsync<List<AbsenceDto>>("/api/me/absences", Erika)).Single().Type.ShouldBe("Sickness");
    }

    [Fact]
    public async Task Vacation_is_requested_approved_and_checked_against_the_balance()
    {
        await using var api = await StartAsync(postgres);
        await api.SetEntitlementAsync(Erika, 2026, 2);

        using var tooLong = await api.SendAsync(HttpMethod.Post, "/api/me/absences/vacation", Erika, new RequestVacationRequest(new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 6)));
        tooLong.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var request = await api.PostAbsenceAsync("/api/me/absences/vacation", Erika, new RequestVacationRequest(new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 3)));
        request.Status.ShouldBe("Requested");

        var pending = await api.GetAsync<List<PendingApprovalDto>>("/api/approvals/pending", Max);
        pending.Single().AbsenceId.ShouldBe(request.Id);
        (await api.GetAsync<List<PendingApprovalDto>>("/api/approvals/pending", Lena)).ShouldBeEmpty();

        using var byStranger = await api.SendAsync(HttpMethod.Post, $"/api/absences/{request.Id}/approve", Lena);
        byStranger.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var approved = await api.PostAbsenceAsync($"/api/absences/{request.Id}/approve", Max);
        approved.Status.ShouldBe("Approved");
        var balance = await api.GetAsync<VacationBalanceDto>("/api/me/vacation-balance?year=2026", Erika);
        balance.Remaining.ShouldBe(0m);
    }

    [Fact]
    public async Task Someone_elses_absence_cannot_be_withdrawn_and_looks_missing()
    {
        await using var api = await StartAsync(postgres);
        await api.SetEntitlementAsync(Erika, 2026, 10);
        var request = await api.PostAbsenceAsync("/api/me/absences/vacation", Erika, new RequestVacationRequest(new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 3)));

        using var foreign = await api.SendAsync(HttpMethod.Post, $"/api/me/absences/{request.Id}/withdraw", Ben);
        foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var own = await api.SendAsync(HttpMethod.Post, $"/api/me/absences/{request.Id}/withdraw", Erika);
        own.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Open_sick_note_can_be_ended_and_overlap_is_a_conflict()
    {
        await using var api = await StartAsync(postgres);
        var open = await api.PostAbsenceAsync("/api/me/absences/sick", Erika, new ReportSickRequest(new DateOnly(2026, 10, 23), null));
        open.To.ShouldBeNull();

        using var overlap = await api.SendAsync(HttpMethod.Post, "/api/me/absences/sick", Erika, SickToday);
        overlap.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var ended = await api.PostAbsenceAsync($"/api/me/absences/{open.Id}/end", Erika, new EndSickRequest(new DateOnly(2026, 10, 26)));
        ended.To.ShouldBe(new DateOnly(2026, 10, 26));
    }

    [Fact]
    public async Task Entitlement_is_maintained_by_hr_only()
    {
        await using var api = await StartAsync(postgres);

        using var manager = await api.SendAsync(HttpMethod.Put, $"/api/employees/{Erika}/vacation-entitlements/2026", Max, new SetEntitlementRequest(30));
        using var hr = await api.SendAsync(HttpMethod.Put, $"/api/employees/{Erika}/vacation-entitlements/2026", Hanna, new SetEntitlementRequest(30));

        manager.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        hr.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await api.GetAsync<VacationBalanceDto>($"/api/employees/{Erika}/vacation-balance?year=2026", Hanna)).Entitlement.ShouldBe(30m);
    }

    [Fact]
    public async Task Sync_status_is_visible_to_hr_and_follows_the_confirmed_version()
    {
        await using var api = await StartAsync(postgres);
        var absence = await api.PostAbsenceAsync("/api/me/absences/sick", Erika, new ReportSickRequest(new DateOnly(2026, 10, 23), null));
        (await api.GetAsync<List<AbsenceDto>>($"/api/employees/{Erika}/absences", Hanna)).Single().SyncStatus.ShouldBe("Pending");

        await api.DeliverAsync(new BcSyncSucceeded(Guid.NewGuid(), Guid.NewGuid(), Start, SourceService.Workforce, BcSyncEntityType.Absence, absence.Id, 1, Guid.NewGuid()));

        (await api.GetAsync<List<AbsenceDto>>($"/api/employees/{Erika}/absences", Hanna)).Single().SyncStatus.ShouldBe("Synced");
        // The own view does not show the BC transfer.
        (await api.GetAsync<List<AbsenceDto>>("/api/me/absences", Erika)).Single().SyncStatus.ShouldBeNull();

        // A change raises the version; the failure of that version marks it, a late confirmation of the old one does not undo it.
        await api.PostAbsenceAsync($"/api/me/absences/{absence.Id}/end", Erika, new EndSickRequest(new DateOnly(2026, 10, 24)));
        await api.DeliverAsync(new BcSyncFailed(Guid.NewGuid(), Guid.NewGuid(), Start, SourceService.Workforce, BcSyncEntityType.Absence, absence.Id, 2, BcSyncErrorCode.Unknown));
        (await api.GetAsync<List<AbsenceDto>>($"/api/employees/{Erika}/absences", Hanna)).Single().SyncStatus.ShouldBe("Failed");
        await api.DeliverAsync(new BcSyncSucceeded(Guid.NewGuid(), Guid.NewGuid(), Start, SourceService.Workforce, BcSyncEntityType.Absence, absence.Id, 1, Guid.NewGuid()));
        (await api.GetAsync<List<AbsenceDto>>($"/api/employees/{Erika}/absences", Hanna)).Single().SyncStatus.ShouldBe("Failed");
    }

    [Fact]
    public async Task Internal_details_need_the_service_role()
    {
        await using var api = await StartAsync(postgres);
        var absence = await api.PostAbsenceAsync("/api/me/absences/sick", Erika, SickToday);
        var url = $"/internal/absences/{absence.Id}";

        using var anonymous = await api.SendAsync(HttpMethod.Get, url, null);
        using var user = await api.SendAsync(HttpMethod.Get, url, Hanna);
        using var wrongRole = await api.SendAsync(HttpMethod.Get, url, null, null, "Other.Role");
        using var allowed = await api.SendAsync(HttpMethod.Get, url, null, null, WorkforceRoles.AbsenceDetailsRead);
        using var missing = await api.SendAsync(HttpMethod.Get, $"/internal/absences/{Guid.NewGuid()}", null, null, WorkforceRoles.AbsenceDetailsRead);

        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        user.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        wrongRole.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await allowed.Content.ReadAsStringAsync()).ShouldContain("Sickness");
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("GET", "/api/me/absences")]
    [InlineData("POST", "/api/me/absences/sick")]
    [InlineData("POST", "/api/me/absences/vacation")]
    [InlineData("GET", "/api/me/vacation-balance")]
    [InlineData("GET", "/api/approvals/pending")]
    [InlineData("GET", "/api/stores/00000000-0000-0000-0000-00000000a017/absences")]
    public async Task Endpoints_require_authentication(string method, string url)
    {
        await using var api = await StartAsync(postgres);

        using var response = await api.SendAsync(new HttpMethod(method), url, null, method == "POST" ? SickToday : null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Person_without_the_own_privilege_is_forbidden()
    {
        await using var api = await StartAsync(postgres);
        var nobody = Guid.NewGuid();

        using var read = await api.SendAsync(HttpMethod.Get, "/api/me/absences", nobody);
        using var sick = await api.SendAsync(HttpMethod.Post, "/api/me/absences/sick", nobody, SickToday);
        using var pending = await api.SendAsync(HttpMethod.Get, "/api/approvals/pending", Erika);

        read.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        sick.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        pending.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
