using System.Net;
using HrTool_API.Authentication;
using HrTool_API.Domain.Entities;
using HrTool_API.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HrTool_API.Tests.Functional;

/// <summary>
/// Abwesenheiten: Genehmigungsstatus und Ablehnungsgrund gibt es nur bei Urlaub (<c>requiresApproval</c>), alle anderen Arten gelten sofort als genehmigt.
/// Dazu Filter, Scope nach Filiale und Validierung.
/// </summary>
public class AbsenceTests(ApiFactory factory) : ApiTestBase(factory)
{
    private static string Code() => ("T" + Guid.NewGuid().ToString("N")[..10]).ToUpperInvariant();

    private Task<AbsenceType> VacationAsync() => InsertAsync(new AbsenceType { Code = Code(), Description = "Urlaub", ScheduleSymbol = "U", RequiresApproval = true });
    private Task<AbsenceType> SickAsync() => InsertAsync(new AbsenceType { Code = Code(), Description = "Krank", ScheduleSymbol = "K" });

    private async Task<Employee> NewEmployeeAsync(Guid? locationId, bool active = true) =>
        await InsertAsync(new Employee
        {
            EmployeeNo = Unique("N"), FirstName = "Vor", LastName = Unique("Nach"), Active = active,
            WorkplaceId = locationId is { } l ? await WorkplaceForAsync(l) : null
        });

    private static async Task<string?> MessageAsync(HttpResponseMessage response) => (await JsonAsync(response)).GetProperty("message").GetString();

    private static List<Guid> Ids(System.Text.Json.JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToList();

    private async Task<Guid> CreateAsync(string token, Guid employeeId, Guid typeId, string from, string to, object? extra = null)
    {
        var response = await PostAsync("/api/absences", Merge(new { employeeId, absenceTypeId = typeId, dateFrom = from, dateTo = to, days = 1 }, extra), token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await DataAsync(response)).GetProperty("id").GetGuid();
    }

    private static Dictionary<string, object?> Merge(object a, object? b)
    {
        var result = new Dictionary<string, object?>();
        foreach (var o in new[] { a, b })
            if (o is not null)
                foreach (var p in o.GetType().GetProperties()) result[p.Name] = p.GetValue(o);
        return result;
    }

    // --- Anlegen ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AVacationRequest_IsStoredWithItsStatus_AndTheTimeItWasRequested()
    {
        var token = await AdminTokenAsync();
        var employee = await NewEmployeeAsync(null);
        var vacation = await VacationAsync();
        var before = DateTime.UtcNow.AddSeconds(-2);

        var response = await PostAsync("/api/absences", new
        {
            employeeId = employee.Id, absenceTypeId = vacation.Id, dateFrom = "2026-12-24", dateTo = "2026-12-29", days = 2, vacationDays = 2,
            approvalStatus = "Vorgesetzter akzeptiert", remark = "Weihnachten"
        }, token);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var data = await DataAsync(response);
        Assert.Equal("Vorgesetzter akzeptiert", data.GetProperty("approvalStatus").GetString());
        Assert.True(data.GetProperty("requiresApproval").GetBoolean());
        Assert.Equal(employee.EmployeeNo, data.GetProperty("employeeNo").GetString());
        Assert.Equal(vacation.Code, data.GetProperty("absenceTypeCode").GetString());
        Assert.Equal("U", data.GetProperty("scheduleSymbol").GetString());
        Assert.Equal(2m, data.GetProperty("days").GetDecimal());
        Assert.True(data.GetProperty("requestedAt").GetDateTime() >= before);       // ohne Angabe: der Zeitpunkt des Anlegens
        Assert.StartsWith("user-", data.GetProperty("recordedBy").GetString());       // „Erfasst von“ ist der angemeldete User, nicht eine Angabe des Clients
    }

    [Fact]
    public async Task OtherTypes_NeedNoApproval_SoStatusAndRejectionReasonAreRefused()
    {
        var token = await AdminTokenAsync();
        var employee = await NewEmployeeAsync(null);
        var sick = await SickAsync();

        var plain = await PostAsync("/api/absences", new { employeeId = employee.Id, absenceTypeId = sick.Id, dateFrom = "2026-10-01", dateTo = "2026-10-03", days = 3 }, token);
        var withStatus = await PostAsync("/api/absences", new { employeeId = employee.Id, absenceTypeId = sick.Id, dateFrom = "2026-10-01", dateTo = "2026-10-03", approvalStatus = "Genehmigt" }, token);
        var id = (await DataAsync(plain)).GetProperty("id").GetGuid();
        var patchStatus = await PatchAsync($"/api/absences/{id}", new { approvalStatus = "Genehmigt" }, token);
        var patchReason = await PatchAsync($"/api/absences/{id}", new { rejectionReason = "nein" }, token);

        Assert.Equal(HttpStatusCode.Created, plain.StatusCode);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, (await DataAsync(plain)).GetProperty("approvalStatus").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, (await DataAsync(plain)).GetProperty("requestedAt").ValueKind);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, withStatus.StatusCode);
        Assert.Equal("approvalStatus", (await JsonAsync(withStatus)).GetProperty("errors")[0].GetProperty("field").GetString());
        Assert.Equal("Nur Urlaubsanträge werden genehmigt.", (await JsonAsync(withStatus)).GetProperty("errors")[0].GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, patchStatus.StatusCode);
        Assert.Equal("rejectionReason", (await JsonAsync(patchReason)).GetProperty("errors")[0].GetProperty("field").GetString());
    }

    [Fact]
    public async Task Create_ValidatesTheInput()
    {
        var token = await AdminTokenAsync();
        var (employee, inactive) = (await NewEmployeeAsync(null), await NewEmployeeAsync(null, active: false));
        var type = await SickAsync();
        object Body(Guid? emp = null, Guid? t = null, string from = "2026-10-05", string to = "2026-10-06", string? tf = null, string? tt = null, decimal days = 1) =>
            new { employeeId = emp ?? employee.Id, absenceTypeId = t ?? type.Id, dateFrom = from, dateTo = to, timeFrom = tf, timeTo = tt, days };

        var reversed = await PostAsync("/api/absences", Body(from: "2026-10-06", to: "2026-10-05"), token);
        var onlyFrom = await PostAsync("/api/absences", Body(tf: "09:00"), token);
        var hours = await PostAsync("/api/absences", Body(tf: "09:00", tt: "13:00"), token);
        var negative = await PostAsync("/api/absences", Body(days: -1), token);
        var missing = await PostAsync("/api/absences", new { }, token);
        var unknownType = await PostAsync("/api/absences", Body(t: Guid.NewGuid()), token);
        var unknownEmployee = await PostAsync("/api/absences", Body(emp: Guid.NewGuid()), token);
        var deactivated = await PostAsync("/api/absences", Body(emp: inactive.Id), token);

        Assert.Equal("dateTo", (await JsonAsync(reversed)).GetProperty("errors")[0].GetProperty("field").GetString());
        Assert.Equal("timeTo", (await JsonAsync(onlyFrom)).GetProperty("errors")[0].GetProperty("field").GetString());
        Assert.Equal(HttpStatusCode.Created, hours.StatusCode);
        Assert.Equal("13:00:00", (await DataAsync(hours)).GetProperty("timeTo").GetString());
        Assert.All(new[] { reversed, onlyFrom, negative, missing, unknownType }, r => Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode));
        Assert.Equal(HttpStatusCode.NotFound, unknownEmployee.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deactivated.StatusCode);
    }

    // --- Genehmigen (PATCH) ----------------------------------------------------------------------------------------

    [Fact]
    public async Task Update_HrDecidesARequest_ApprovedOrRejectedWithAReason()
    {
        var token = await AdminTokenAsync();
        var employee = await NewEmployeeAsync(null);
        var vacation = await VacationAsync();
        var id = await CreateAsync(token, employee.Id, vacation.Id, "2026-11-09", "2026-11-13", new { approvalStatus = "Vorgesetzter akzeptiert", days = 5, vacationDays = 5 });

        var rejected = await PatchAsync($"/api/absences/{id}", new { approvalStatus = "Abgelehnt", rejectionReason = "Zeitraum bereits ausgebucht", vacationDays = 0 }, token);

        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        var data = await DataAsync(rejected);
        Assert.Equal("Abgelehnt", data.GetProperty("approvalStatus").GetString());
        Assert.Equal("Zeitraum bereits ausgebucht", data.GetProperty("rejectionReason").GetString());
        Assert.Equal(0m, data.GetProperty("vacationDays").GetDecimal());
        Assert.Equal(5m, data.GetProperty("days").GetDecimal());                       // unverändert
        Assert.Equal(employee.Id, data.GetProperty("employeeId").GetGuid());
    }

    [Fact]
    public async Task Update_ChecksTheResultingPeriod_AndNeverChangesEmployeeOrType()
    {
        var token = await AdminTokenAsync();
        var employee = await NewEmployeeAsync(null);
        var sick = await SickAsync();
        var id = await CreateAsync(token, employee.Id, sick.Id, "2026-10-05", "2026-10-07");

        var tooEarly = await PatchAsync($"/api/absences/{id}", new { dateTo = "2026-10-01" }, token);   // vor dem bisherigen Beginn
        var ok = await PatchAsync($"/api/absences/{id}", new { dateTo = "2026-10-09", employeeId = Guid.NewGuid(), absenceTypeId = Guid.NewGuid() }, token);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooEarly.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var data = await DataAsync(ok);
        Assert.Equal("2026-10-09", data.GetProperty("dateTo").GetString());
        Assert.Equal(employee.Id, data.GetProperty("employeeId").GetGuid());
        Assert.Equal(sick.Id, data.GetProperty("absenceTypeId").GetGuid());
    }

    // --- Lesen ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task List_FiltersByEmployeeTypePeriodStatusAndLocation_NewestFirst()
    {
        var token = await AdminTokenAsync();
        var (a, b) = (CreateLocation(), CreateLocation());
        var (empA, empB) = (await NewEmployeeAsync(a.Id), await NewEmployeeAsync(b.Id));
        var (vacation, sick) = (await VacationAsync(), await SickAsync());
        var early = await CreateAsync(token, empA.Id, vacation.Id, "2090-03-02", "2090-03-06", new { approvalStatus = "Vorgesetzter akzeptiert" });
        var late = await CreateAsync(token, empA.Id, sick.Id, "2090-03-20", "2090-03-22");
        var other = await CreateAsync(token, empB.Id, vacation.Id, "2090-03-04", "2090-03-05", new { approvalStatus = "Genehmigt" });
        var range = "from=2090-03-01&to=2090-03-31";

        Assert.Equal([late, early], Ids(await DataAsync(await GetAsync($"/api/absences?employeeId={empA.Id}&{range}", token))));
        Assert.Equal([early], Ids(await DataAsync(await GetAsync($"/api/absences?employeeId={empA.Id}&absenceTypeId={vacation.Id}", token))));
        Assert.Equal([other, early], Ids(await DataAsync(await GetAsync($"/api/absences?absenceTypeId={vacation.Id}&{range}", token))));
        Assert.Equal([early], Ids(await DataAsync(await GetAsync($"/api/absences?approvalStatus=Vorgesetzter%20akzeptiert&{range}", token))));
        Assert.Equal([other], Ids(await DataAsync(await GetAsync($"/api/absences?locationId={b.Id}&{range}", token))));
        Assert.Equal([other], Ids(await DataAsync(await GetAsync($"/api/absences?workplaceId={await WorkplaceForAsync(b.Id)}&{range}", token))));
        Assert.Equal([early, other], Ids(await DataAsync(await GetAsync($"/api/absences?from=2090-03-05&to=2090-03-05&sortOrder=asc", token))).OrderBy(i => i == early ? 0 : 1).ToList()); // berührt den 5.: beide Urlaube
    }

    [Fact]
    public async Task ScopedUser_SeesOnlyAbsencesOfEmployeesOfTheirLocations()
    {
        var (mine, other) = (CreateLocation(), CreateLocation());
        var admin = await AdminTokenAsync();
        var sick = await SickAsync();
        var (own, foreign) = (await NewEmployeeAsync(mine.Id), await NewEmployeeAsync(other.Id));
        var ownAbsence = await CreateAsync(admin, own.Id, sick.Id, "2091-01-05", "2091-01-06");
        var foreignAbsence = await CreateAsync(admin, foreign.Id, sick.Id, "2091-01-05", "2091-01-06");
        var (_, token) = await ScopedUserAsync(["absence:read", "absence:create", "absence:update", "absence:delete"], [mine.Id]);

        Assert.Equal([ownAbsence], Ids(await DataAsync(await GetAsync("/api/absences?from=2091-01-01&to=2091-01-31", token))));
        var detail = await GetAsync($"/api/absences/{foreignAbsence}", token);
        Assert.Equal(HttpStatusCode.Forbidden, detail.StatusCode);
        Assert.Equal(AuthMessages.Read, await MessageAsync(detail));
        Assert.Equal(HttpStatusCode.Forbidden, (await PatchAsync($"/api/absences/{foreignAbsence}", new { remark = "x" }, token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await DeleteAsync($"/api/absences/{foreignAbsence}", token)).StatusCode);
        var create = await PostAsync("/api/absences", new { employeeId = foreign.Id, absenceTypeId = sick.Id, dateFrom = "2091-01-09", dateTo = "2091-01-09" }, token);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(AuthMessages.Create, await MessageAsync(create));
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync($"/api/absences/{Guid.NewGuid()}", token)).StatusCode);
    }

    [Fact]
    public async Task Delete_RemovesTheAbsence_AndReadOnlyUsersCannotChangeAnything()
    {
        var token = await AdminTokenAsync();
        var employee = await NewEmployeeAsync(null);
        var sick = await SickAsync();
        var id = await CreateAsync(token, employee.Id, sick.Id, "2092-02-02", "2092-02-02");
        var (_, readOnly) = await UserWithPrivilegesAsync("absence:read");

        Assert.Equal(HttpStatusCode.OK, (await GetAsync($"/api/absences/{id}", readOnly)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await DeleteAsync($"/api/absences/{id}", readOnly)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync($"/api/absences/{id}", token)).StatusCode);
        Assert.False(await Factory.WithDbAsync((db, _) => db.Absences.AnyAsync(a => a.Id == id)));
        Assert.Equal(HttpStatusCode.NotFound, (await DeleteAsync($"/api/absences/{id}", token)).StatusCode);
    }
}
