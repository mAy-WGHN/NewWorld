using System.Net;
using System.Text.Json;
using HrTool_API.Authentication;
using HrTool_API.Domain.Entities;
using HrTool_API.Tests.Infrastructure;

namespace HrTool_API.Tests.Functional;

/// <summary>
/// Die Dienstplan-Matrix und die Stunden eines Plans. Erwartete Werte folgen einem fertigen BC-Dienstplan:
/// Pause ab über 6 h, Nicht-Verfügbarkeit mit Zeiten zählt, ohne Zeiten nicht, Soll = Wochenstunden × Tage / 7, Rand von 6 Tagen mit Schichten der Nachbarpläne.
/// </summary>
public class MatrixTests(ApiFactory factory) : ApiTestBase(factory)
{
    private static int _counter;

    // Eigene Jahre je Test (2000er bis 2090er), damit (Filiale, Jahr, Monat) nie kollidiert.
    private static int NextYear() => 2001 + Interlocked.Increment(ref _counter) % 90;

    private async Task<string> AdminAsync() => (await UserWithPrivilegesAsync([.. PrivilegeCatalog.All])).Token;

    private async Task<Guid> CreatePlanAsync(string token, Guid locationId, int year, int month)
    {
        var response = await PostAsync("/api/schedules", new { number = Unique("DP"), workplaceId = await WorkplaceForAsync(locationId), year, month }, token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await DataAsync(response)).GetProperty("id").GetGuid();
    }

    private async Task<T> InsertAsync<T>(T entity) where T : class =>
        await Factory.WithDbAsync(async (db, _) =>
        {
            db.Add(entity);
            await db.SaveChangesAsync();
            return entity;
        });

    private async Task<Employee> NewEmployeeAsync(Guid? locationId, string last = "Muster", Guid? contractId = null, bool active = true) =>
        await InsertAsync(new Employee
        {
            EmployeeNo = Unique("N"), FirstName = "Vor", LastName = last, ContractId = contractId, Active = active,
            WorkplaceId = locationId is { } l ? await WorkplaceForAsync(l) : null
        });

    private Task<AbsenceType> NewAbsenceTypeAsync(string symbol) =>
        InsertAsync(new AbsenceType { Code = Unique("ART").ToUpperInvariant(), Description = "x", ScheduleSymbol = symbol });

    private async Task ShiftAsync(string token, Guid planId, Guid employeeId, string date, string? start = null, string? end = null, Guid? absenceTypeId = null, string? code = null) =>
        Assert.Equal(HttpStatusCode.Created, (await PostAsync($"/api/schedules/{planId}/shifts",
            new { employeeId, date, startTime = start, endTime = end, absenceTypeId, shiftCode = code }, token)).StatusCode);

    private static async Task<string?> MessageAsync(HttpResponseMessage response) => (await JsonAsync(response)).GetProperty("message").GetString();

    // --- Stunden ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Detail_PlannedHours_SubtractBreaks_AbsenceWithTimesCounts_WithoutTimesOrCodeOnlyDoNot()
    {
        var token = await AdminAsync();
        var location = CreateLocation();
        var year = NextYear();
        var planId = await CreatePlanAsync(token, location.Id, year, 10);
        var employee = await NewEmployeeAsync(location.Id);
        var vacation = await NewAbsenceTypeAsync("U");

        await ShiftAsync(token, planId, employee.Id, $"{year}-10-01", "09:00", "17:00");                      // 8 h − 30 min = 7,5
        await ShiftAsync(token, planId, employee.Id, $"{year}-10-02", "05:30", "14:30");                      // 9 h − 30 min = 8,5
        await ShiftAsync(token, planId, employee.Id, $"{year}-10-03", "14:00", "20:00", vacation.Id);         // Urlaub mit Zeiten: 6 h, keine Pause
        await ShiftAsync(token, planId, employee.Id, $"{year}-10-04", absenceTypeId: vacation.Id);            // Urlaub ohne Zeiten: 0
        await ShiftAsync(token, planId, employee.Id, $"{year}-10-05", code: "VKA1");                          // Schichtcode ohne Zeiten: 0

        var detail = await DataAsync(await GetAsync($"/api/schedules/{planId}", token));

        Assert.Equal(22m, detail.GetProperty("plannedHours").GetDecimal());
    }

    // --- Matrix -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Matrix_ListsTheEmployeesOfTheLocation_WithTargetPlannedAndPercent()
    {
        var token = await AdminAsync();
        var location = CreateLocation();
        var year = NextYear();
        var planId = await CreatePlanAsync(token, location.Id, year, 10);
        var contract = await InsertAsync(new EmploymentContract { Code = Unique("TZ"), WeeklyHours = 30m });
        var worker = await NewEmployeeAsync(location.Id, "Berg", contract.Id);
        var idle = await NewEmployeeAsync(location.Id, "Zett");
        await NewEmployeeAsync(location.Id, "Inaktiv", active: false);
        await ShiftAsync(token, planId, worker.Id, $"{year}-10-01", "09:00", "17:00");
        await ShiftAsync(token, planId, worker.Id, $"{year}-10-02", "16:00", "00:00");   // über Mitternacht: 8 h − 30 min

        var matrix = await DataAsync(await GetAsync($"/api/schedules/{planId}/matrix", token));

        var rows = matrix.GetProperty("rows").EnumerateArray().ToList();
        Assert.Equal([worker.Id, idle.Id], rows.Select(r => r.GetProperty("employeeId").GetGuid())); // nach Nachname; Inaktive fehlen
        var first = rows[0];
        Assert.True(first.GetProperty("homeEmployee").GetBoolean());
        Assert.Equal(132.86m, first.GetProperty("targetHours").GetDecimal());   // 30 h × 31 Tage / 7
        Assert.Equal(15m, first.GetProperty("plannedHours").GetDecimal());
        Assert.Equal(11.29m, first.GetProperty("plannedPercent").GetDecimal());
        Assert.Equal(2, first.GetProperty("shifts").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, rows[1].GetProperty("targetHours").ValueKind);
        Assert.Equal(0m, rows[1].GetProperty("plannedHours").GetDecimal());
        Assert.Equal(15m, matrix.GetProperty("schedule").GetProperty("plannedHours").GetDecimal());
        Assert.Equal($"{year}-09-25", matrix.GetProperty("from").GetString()); // 6 Tage Rand vor dem Monat
        Assert.Equal($"{year}-11-06", matrix.GetProperty("to").GetString());
    }

    [Fact]
    public async Task Matrix_ShowsTheMarginFromNeighbourPlans_ButOnlyTheMonthCounts()
    {
        var token = await AdminAsync();
        var location = CreateLocation();
        var year = NextYear();
        var october = await CreatePlanAsync(token, location.Id, year, 10);
        var september = await CreatePlanAsync(token, location.Id, year, 9);
        var employee = await NewEmployeeAsync(location.Id);
        await ShiftAsync(token, september, employee.Id, $"{year}-09-28", "09:00", "17:00");   // im Rand von Oktober
        await ShiftAsync(token, september, employee.Id, $"{year}-09-15", "09:00", "17:00");   // außerhalb des Rands
        await ShiftAsync(token, october, employee.Id, $"{year}-10-01", "09:00", "17:00");

        var matrix = await DataAsync(await GetAsync($"/api/schedules/{october}/matrix?marginDays=6", token));
        var row = matrix.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("employeeId").GetGuid() == employee.Id);

        Assert.Equal([$"{year}-09-28", $"{year}-10-01"], row.GetProperty("shifts").EnumerateArray().Select(s => s.GetProperty("date").GetString()));
        Assert.Equal(7.5m, row.GetProperty("plannedHours").GetDecimal());

        var noMargin = await DataAsync(await GetAsync($"/api/schedules/{october}/matrix?marginDays=0", token));
        var rowWithoutMargin = noMargin.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("employeeId").GetGuid() == employee.Id);
        Assert.Equal(1, rowWithoutMargin.GetProperty("shifts").GetArrayLength());
    }

    [Fact]
    public async Task Matrix_ShowsVisitingEmployees_AndWhereTheOwnEmployeesWorkElsewhere()
    {
        var token = await AdminAsync();
        var (home, other) = (CreateLocation(), CreateLocation());
        var year = NextYear();
        var homePlan = await CreatePlanAsync(token, home.Id, year, 10);
        var otherPlan = await CreatePlanAsync(token, other.Id, year, 10);
        var own = await NewEmployeeAsync(home.Id);
        var visitor = await NewEmployeeAsync(other.Id);
        await ShiftAsync(token, homePlan, visitor.Id, $"{year}-10-07", "10:00", "18:00");   // Aushilfe von der anderen Filiale
        await ShiftAsync(token, otherPlan, own.Id, $"{year}-10-03", "11:00", "20:00");      // eigener Mitarbeiter anderswo („YY …“ in BC)
        var (_, scoped) = await ScopedUserAsync(["schedule:read"], [home.Id]);              // sieht nur die eigene Filiale

        var matrix = await DataAsync(await GetAsync($"/api/schedules/{homePlan}/matrix", scoped));
        var rows = matrix.GetProperty("rows").EnumerateArray().ToList();

        var visitorRow = rows.Single(r => r.GetProperty("employeeId").GetGuid() == visitor.Id);
        Assert.False(visitorRow.GetProperty("homeEmployee").GetBoolean());
        Assert.Equal(JsonValueKind.Null, visitorRow.GetProperty("targetHours").ValueKind);
        Assert.Equal(7.5m, visitorRow.GetProperty("plannedHours").GetDecimal());
        var ownRow = rows.Single(r => r.GetProperty("employeeId").GetGuid() == own.Id);
        var away = Assert.Single(ownRow.GetProperty("away").EnumerateArray());
        Assert.Equal(await WorkplaceForAsync(other.Id), away.GetProperty("workplaceId").GetGuid());
        Assert.Equal($"{year}-10-03", away.GetProperty("date").GetString());
        Assert.Equal(0m, ownRow.GetProperty("plannedHours").GetDecimal());                  // zählt nicht in diesem Plan
        Assert.Equal(0, ownRow.GetProperty("shifts").GetArrayLength());
    }

    [Fact]
    public async Task Matrix_ListsTheAbsenceTypesItUses_AndRespectsScopeAndMargin()
    {
        var token = await AdminAsync();
        var (a, b) = (CreateLocation(), CreateLocation());
        var year = NextYear();
        var plan = await CreatePlanAsync(token, b.Id, year, 10);
        var employee = await NewEmployeeAsync(b.Id);
        var type = await NewAbsenceTypeAsync("KU");
        await ShiftAsync(token, plan, employee.Id, $"{year}-10-02", absenceTypeId: type.Id);
        var (_, outside) = await ScopedUserAsync(["schedule:read"], [a.Id]);

        var matrix = await DataAsync(await GetAsync($"/api/schedules/{plan}/matrix", token));
        var forbidden = await GetAsync($"/api/schedules/{plan}/matrix", outside);

        var used = Assert.Single(matrix.GetProperty("absenceTypes").EnumerateArray());
        Assert.Equal("KU", used.GetProperty("symbol").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(AuthMessages.Read, await MessageAsync(forbidden));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await GetAsync($"/api/schedules/{plan}/matrix?marginDays=15", token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync($"/api/schedules/{Guid.NewGuid()}/matrix", token)).StatusCode);
    }
}
