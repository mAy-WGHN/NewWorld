using System.Net;
using System.Text.Json;
using HrTool_API.Authentication;
using HrTool_API.Domain.Entities;
using HrTool_API.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HrTool_API.Tests.Functional;

/// <summary>Dienstpläne und Schichten: Rechte (schedule:*), Scope nach Filiale, Validierung und die vordefinierten Fehlermeldungen.</summary>
public class ScheduleTests(ApiFactory factory) : ApiTestBase(factory)
{
    private static int _month;

    // Jeder Test bekommt eigene Monate, damit die Eindeutigkeit (Filiale, Jahr, Monat) nie kollidiert; Filialen sind ohnehin je Test neu.
    private static int NextYear() => 2100 - Interlocked.Increment(ref _month) % 90;

    private async Task<string> AdminAsync() => (await UserWithPrivilegesAsync([.. PrivilegeCatalog.All])).Token;

    private async Task<JsonElement> CreateScheduleAsync(string token, Guid locationId, int? year = null, int month = 10, string? number = null, string? status = null)
    {
        var response = await PostAsync("/api/schedules",
            new { number = number ?? Unique("DP"), workplaceId = await WorkplaceForAsync(locationId), year = year ?? NextYear(), month, status, budgetHours = 100.5 }, token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await DataAsync(response);
    }

    private async Task<Employee> CreateEmployeeAsync(Guid? locationId, bool active = true)
    {
        var workplaceId = locationId is { } l ? await WorkplaceForAsync(l) : (Guid?)null;
        return await Factory.WithDbAsync(async (db, _) =>
        {
            var e = new Employee { EmployeeNo = string.Concat(Enumerable.Range(0, 8).Select(_ => Random.Shared.Next(1, 10))), FirstName = "V", LastName = "N", WorkplaceId = workplaceId, Active = active };
            db.Employees.Add(e);
            await db.SaveChangesAsync();
            return e;
        });
    }

    private async Task<T> InsertAsync<T>(T entity) where T : class =>
        await Factory.WithDbAsync(async (db, _) =>
        {
            db.Add(entity);
            await db.SaveChangesAsync();
            return entity;
        });

    private static async Task<string?> MessageAsync(HttpResponseMessage response) => (await JsonAsync(response)).GetProperty("message").GetString();

    // --- Anlegen ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Create_ReturnsTheDraftPlan_WithTheEnvelope()
    {
        var location = CreateLocation();
        var token = await AdminAsync();

        var response = await PostAsync("/api/schedules", new { number = " DPAB0000039 ", workplaceId = await WorkplaceForAsync(location.Id), year = 2099, month = 4 }, token);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var data = await DataAsync(response);
        Assert.Equal("DPAB0000039", data.GetProperty("number").GetString());
        Assert.Equal("draft", data.GetProperty("status").GetString());
        Assert.Equal(0m, data.GetProperty("budgetHours").GetDecimal());
        Assert.Equal(location.Id, data.GetProperty("locationId").GetGuid());
        Assert.True((await JsonAsync(response)).GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Create_WithMissingOrInvalidFields_Is422WithGermanMessages()
    {
        var token = await AdminAsync();

        var response = await PostAsync("/api/schedules", new { year = 1999, month = 13 }, token);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var errors = (await JsonAsync(response)).GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("field").GetString()).ToList();
        Assert.Contains("number", errors);
        Assert.Contains("workplaceId", errors);
        Assert.Contains("year", errors);
        Assert.Contains("month", errors);
    }

    [Fact]
    public async Task Create_ForAnUnknownOrArchivedLocation_Is404()
    {
        var token = await AdminAsync();
        var archived = CreateLocation(archived: true);

        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync("/api/schedules", new { number = Unique("DP"), workplaceId = Guid.NewGuid(), year = NextYear(), month = 1 }, token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync("/api/schedules", new { number = Unique("DP"), workplaceId = await WorkplaceForAsync(archived.Id), year = NextYear(), month = 1 }, token)).StatusCode);
    }

    [Fact]
    public async Task Create_TheSameLocationAndMonthTwice_OrTheSameNumber_Is409()
    {
        var token = await AdminAsync();
        var location = CreateLocation();
        var year = NextYear();
        var number = Unique("DP");
        await CreateScheduleAsync(token, location.Id, year, 5, number);

        var sameMonth = await PostAsync("/api/schedules", new { number = Unique("DP"), workplaceId = await WorkplaceForAsync(location.Id), year, month = 5 }, token);
        var sameNumber = await PostAsync("/api/schedules", new { number, workplaceId = await WorkplaceForAsync(CreateLocation().Id), year, month = 6 }, token);

        Assert.Equal(HttpStatusCode.Conflict, sameMonth.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, sameNumber.StatusCode);
    }

    [Fact]
    public async Task Create_ForALocationOutsideTheScope_Is403WithThePredefinedMessage()
    {
        var (mine, other) = (CreateLocation(), CreateLocation());
        var (_, token) = await ScopedUserAsync(["schedule:create"], [mine.Id]);

        var denied = await PostAsync("/api/schedules", new { number = Unique("DP"), workplaceId = await WorkplaceForAsync(other.Id), year = NextYear(), month = 1 }, token);
        var allowed = await PostAsync("/api/schedules", new { number = Unique("DP"), workplaceId = await WorkplaceForAsync(mine.Id), year = NextYear(), month = 1 }, token);

        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(AuthMessages.Create, await MessageAsync(denied));
        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
    }

    // --- Lesen / Scope -----------------------------------------------------------------------------------------

    [Fact]
    public async Task List_ShowsOnlyThePlansOfTheLocationsInTheScope_AndFiltersWork()
    {
        var (a, b) = (CreateLocation(), CreateLocation());
        var admin = await AdminAsync();
        var year = NextYear();
        var inA = await CreateScheduleAsync(admin, a.Id, year, 3);
        var inB = await CreateScheduleAsync(admin, b.Id, year, 3);
        var (_, scoped) = await ScopedUserAsync(["schedule:read"], [a.Id]);

        var scopedItems = (await DataAsync(await GetAsync($"/api/schedules?year={year}", scoped))).GetProperty("items").EnumerateArray().Select(s => s.GetProperty("id").GetGuid()).ToList();
        var adminItems = (await DataAsync(await GetAsync($"/api/schedules?year={year}&month=3", admin))).GetProperty("items").EnumerateArray().Select(s => s.GetProperty("id").GetGuid()).ToList();
        var byLocation = (await DataAsync(await GetAsync($"/api/schedules?locationId={b.Id}", admin))).GetProperty("items").EnumerateArray().Select(s => s.GetProperty("id").GetGuid()).ToList();

        Assert.Equal([inA.GetProperty("id").GetGuid()], scopedItems);
        Assert.Contains(inA.GetProperty("id").GetGuid(), adminItems);
        Assert.Contains(inB.GetProperty("id").GetGuid(), adminItems);
        Assert.Equal([inB.GetProperty("id").GetGuid()], byLocation);
    }

    [Fact]
    public async Task List_WithAnInvalidMonthFilter_Is422()
    {
        var token = await AdminAsync();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await GetAsync("/api/schedules?month=13", token)).StatusCode);
    }

    [Fact]
    public async Task Detail_OutsideTheScopeIs403_AndAnUnknownIdIs404()
    {
        var (a, b) = (CreateLocation(), CreateLocation());
        var plan = await CreateScheduleAsync(await AdminAsync(), b.Id);
        var (_, scoped) = await ScopedUserAsync(["schedule:read"], [a.Id]);

        var outside = await GetAsync($"/api/schedules/{plan.GetProperty("id").GetGuid()}", scoped);
        var unknown = await GetAsync($"/api/schedules/{Guid.NewGuid()}", scoped);

        Assert.Equal(HttpStatusCode.Forbidden, outside.StatusCode);
        Assert.Equal(AuthMessages.Read, await MessageAsync(outside));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task AnEmptyScope_SeesNothing_FailClosed()
    {
        var plan = await CreateScheduleAsync(await AdminAsync(), CreateLocation().Id);
        var (_, token) = await ScopedUserAsync(["schedule:read"], ScopeSet.Of([]));

        var items = (await DataAsync(await GetAsync("/api/schedules", token))).GetProperty("items").EnumerateArray().Select(s => s.GetProperty("id").GetGuid());

        Assert.DoesNotContain(plan.GetProperty("id").GetGuid(), items);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync($"/api/schedules/{plan.GetProperty("id").GetGuid()}", token)).StatusCode);
    }

    // --- Ändern / Löschen ----------------------------------------------------------------------------------------

    [Fact]
    public async Task Update_ChangesStatusAndBudget_ButNotNumberLocationOrMonth()
    {
        var location = CreateLocation();
        var token = await AdminAsync();
        var plan = await CreateScheduleAsync(token, location.Id, month: 2);
        var id = plan.GetProperty("id").GetGuid();

        var response = await PatchAsync($"/api/schedules/{id}", new { status = "confirmed", budgetHours = 250, number = "X", month = 9 }, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await DataAsync(response);
        Assert.Equal("confirmed", data.GetProperty("status").GetString());
        Assert.Equal(250m, data.GetProperty("budgetHours").GetDecimal());
        Assert.Equal(plan.GetProperty("number").GetString(), data.GetProperty("number").GetString());
        Assert.Equal(2, data.GetProperty("month").GetInt32());
    }

    [Fact]
    public async Task Update_OutsideTheScope_Is403_AndLeavesThePlanUntouched()
    {
        var (a, b) = (CreateLocation(), CreateLocation());
        var plan = await CreateScheduleAsync(await AdminAsync(), b.Id);
        var id = plan.GetProperty("id").GetGuid();
        var (_, scoped) = await ScopedUserAsync(["schedule:update"], [a.Id]);

        var response = await PatchAsync($"/api/schedules/{id}", new { status = "confirmed" }, scoped);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(AuthMessages.Update, await MessageAsync(response));
        Assert.Equal(ScheduleStatus.Draft, (await Factory.WithDbAsync((db, _) => db.Schedules.IgnoreQueryFilters().SingleAsync(s => s.Id == id))).Status);
    }

    [Fact]
    public async Task Delete_OnlyDraftsAreDeleted_AndTheirShiftsGoWithThem()
    {
        var token = await AdminAsync();
        var draft = (await CreateScheduleAsync(token, CreateLocation().Id)).GetProperty("id").GetGuid();
        var confirmed = (await CreateScheduleAsync(token, CreateLocation().Id, status: "confirmed")).GetProperty("id").GetGuid();
        var employee = await CreateEmployeeAsync(null);
        await InsertAsync(new Shift { ScheduleId = draft, EmployeeId = employee.Id, Date = new DateOnly(2099, 1, 1) });

        Assert.Equal(HttpStatusCode.Conflict, (await DeleteAsync($"/api/schedules/{confirmed}", token)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync($"/api/schedules/{draft}", token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync($"/api/schedules/{draft}", token)).StatusCode);
        Assert.False(await Factory.WithDbAsync((db, _) => db.Shifts.AnyAsync(s => s.ScheduleId == draft)));
    }

    // --- Schichten --------------------------------------------------------------------------------------------

    private async Task<(string Token, Guid PlanId, Employee Employee)> PlanWithEmployeeAsync(int month = 10)
    {
        var token = await AdminAsync();
        var location = CreateLocation();
        var plan = await CreateScheduleAsync(token, location.Id, 2098, month, Unique("DP"));
        return (token, plan.GetProperty("id").GetGuid(), await CreateEmployeeAsync(location.Id));
    }

    [Fact]
    public async Task Shift_CanBeCreated_ListedFiltered_Changed_AndDeleted()
    {
        var token = await AdminAsync();
        var location = CreateLocation();
        var planId = (await CreateScheduleAsync(token, location.Id, NextYear(), 10)).GetProperty("id").GetGuid();
        var plan = await Factory.WithDbAsync((db, _) => db.Schedules.SingleAsync(s => s.Id == planId));
        var (e1, e2) = (await CreateEmployeeAsync(location.Id), await CreateEmployeeAsync(location.Id));
        var day = $"{plan.Year}-10-04";

        var created = await PostAsync($"/api/schedules/{planId}/shifts", new { employeeId = e1.Id, date = day, startTime = "09:00", endTime = "19:00", shiftCode = " VKA1 " }, token);
        await PostAsync($"/api/schedules/{planId}/shifts", new { employeeId = e2.Id, date = $"{plan.Year}-10-05", shiftCode = "VKA1" }, token);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var shift = await DataAsync(created);
        var shiftId = shift.GetProperty("id").GetGuid();
        Assert.Equal("VKA1", shift.GetProperty("shiftCode").GetString());
        Assert.Equal("09:00:00", shift.GetProperty("startTime").GetString());

        var all = (await DataAsync(await GetAsync($"/api/schedules/{planId}/shifts", token))).EnumerateArray().ToList();
        var ofE2 = (await DataAsync(await GetAsync($"/api/schedules/{planId}/shifts?employeeId={e2.Id}", token))).EnumerateArray().ToList();
        var fromFifth = (await DataAsync(await GetAsync($"/api/schedules/{planId}/shifts?from={plan.Year}-10-05", token))).EnumerateArray().ToList();
        Assert.Equal(2, all.Count);
        Assert.Single(ofE2);
        Assert.Single(fromFifth);

        var patched = await PatchAsync($"/api/schedules/{planId}/shifts/{shiftId}", new { endTime = "17:15", remark = "früher Schluss" }, token);
        Assert.Equal(HttpStatusCode.OK, patched.StatusCode);
        var data = await DataAsync(patched);
        Assert.Equal("17:15:00", data.GetProperty("endTime").GetString());
        Assert.Equal("09:00:00", data.GetProperty("startTime").GetString());
        Assert.Equal("VKA1", data.GetProperty("shiftCode").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync($"/api/schedules/{planId}/shifts/{shiftId}", token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await DeleteAsync($"/api/schedules/{planId}/shifts/{shiftId}", token)).StatusCode);
    }

    [Fact]
    public async Task Shift_Validation_DateInTheMonth_TimesTogether_AndKnownReferences()
    {
        var (token, planId, employee) = await PlanWithEmployeeAsync(10);
        var url = $"/api/schedules/{planId}/shifts";

        var otherMonth = await PostAsync(url, new { employeeId = employee.Id, date = "2098-11-01" }, token);
        var onlyStart = await PostAsync(url, new { employeeId = employee.Id, date = "2098-10-01", startTime = "09:00" }, token);
        var badRole = await PostAsync(url, new { employeeId = employee.Id, date = "2098-10-01", planningRoleId = Guid.NewGuid() }, token);
        var badType = await PostAsync(url, new { employeeId = employee.Id, date = "2098-10-01", absenceTypeId = Guid.NewGuid() }, token);
        var missing = await PostAsync(url, new { }, token);

        Assert.All(new[] { otherMonth, onlyStart, badRole, badType, missing }, r => Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode));
        Assert.Equal("date", (await JsonAsync(otherMonth)).GetProperty("errors")[0].GetProperty("field").GetString());
        Assert.Equal("endTime", (await JsonAsync(onlyStart)).GetProperty("errors")[0].GetProperty("field").GetString());
    }

    [Fact]
    public async Task Shift_ForAnUnknownOrDeactivatedEmployee_Is404_ButAnEmployeeOfAnotherLocationIsAllowed()
    {
        var (token, planId, _) = await PlanWithEmployeeAsync();
        var url = $"/api/schedules/{planId}/shifts";
        var deactivated = await CreateEmployeeAsync(null, active: false);
        var visiting = await CreateEmployeeAsync(CreateLocation().Id);

        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(url, new { employeeId = Guid.NewGuid(), date = "2098-10-01" }, token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(url, new { employeeId = deactivated.Id, date = "2098-10-01" }, token)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync(url, new { employeeId = visiting.Id, date = "2098-10-01" }, token)).StatusCode);
    }

    [Fact]
    public async Task Shift_APatchThatBreaksTheRules_IsRejected_AndAShiftOfAnotherPlanIs404()
    {
        var (token, planId, employee) = await PlanWithEmployeeAsync();
        var (_, otherPlanId, _) = await PlanWithEmployeeAsync(11);
        var shiftId = (await DataAsync(await PostAsync($"/api/schedules/{planId}/shifts", new { employeeId = employee.Id, date = "2098-10-01", startTime = "09:00", endTime = "17:00" }, token))).GetProperty("id").GetGuid();

        var outOfMonth = await PatchAsync($"/api/schedules/{planId}/shifts/{shiftId}", new { date = "2098-12-01" }, token);
        var wrongPlan = await PatchAsync($"/api/schedules/{otherPlanId}/shifts/{shiftId}", new { remark = "x" }, token);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, outOfMonth.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, wrongPlan.StatusCode);
    }

    [Fact]
    public async Task Shifts_FollowTheScopeOfThePlan_ReadNeedsReadAndChangesNeedUpdate()
    {
        var (a, b) = (CreateLocation(), CreateLocation());
        var admin = await AdminAsync();
        var planInB = (await CreateScheduleAsync(admin, b.Id, 2097, 10)).GetProperty("id").GetGuid();
        var employee = await CreateEmployeeAsync(b.Id);
        await PostAsync($"/api/schedules/{planInB}/shifts", new { employeeId = employee.Id, date = "2097-10-01" }, admin);
        var (_, scopedRead) = await ScopedUserAsync(["schedule:read", "schedule:update"], [a.Id]);
        var (_, readOnly) = await UserWithPrivilegesAsync("schedule:read");

        var list = await GetAsync($"/api/schedules/{planInB}/shifts", scopedRead);
        var create = await PostAsync($"/api/schedules/{planInB}/shifts", new { employeeId = employee.Id, date = "2097-10-02" }, scopedRead);
        var withoutUpdate = await PostAsync($"/api/schedules/{planInB}/shifts", new { employeeId = employee.Id, date = "2097-10-02" }, readOnly);

        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Equal(AuthMessages.Read, await MessageAsync(list));
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(AuthMessages.Update, await MessageAsync(create));
        Assert.Equal(HttpStatusCode.Forbidden, withoutUpdate.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync($"/api/schedules/{planInB}/shifts", readOnly)).StatusCode);
    }
}
