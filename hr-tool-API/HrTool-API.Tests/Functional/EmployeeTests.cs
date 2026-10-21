using System.Net;
using HrTool_API.Authentication;
using HrTool_API.Domain.Entities;
using HrTool_API.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HrTool_API.Tests.Functional;

/// <summary>Mitarbeiter: Lesen mit Filtern, Pflege dessen, was HR selbst verantwortet, Deaktivieren, Scope nach Filiale.</summary>
public class EmployeeTests(ApiFactory factory) : ApiTestBase(factory)
{
    private async Task<Employee> NewEmployeeAsync(Guid? locationId, string? last = null, string? first = null, bool active = true) =>
        await InsertAsync(new Employee
        {
            EmployeeNo = Unique("N"), FirstName = first ?? "Vor", LastName = last ?? Unique("Nach"), Active = active,
            WorkplaceId = locationId is { } l ? await WorkplaceForAsync(l) : null
        });

    private static async Task<string?> MessageAsync(HttpResponseMessage response) => (await JsonAsync(response)).GetProperty("message").GetString();

    private static List<Guid> Ids(System.Text.Json.JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToList();

    // --- Lesen --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task List_ShowsEmployeesWithTheCodesOfTheirMasterData_SortedByName()
    {
        var token = await AdminTokenAsync();
        var location = CreateLocation();
        var role = await InsertAsync(new PlanningRole { Code = Unique("R").ToUpperInvariant() });
        var contract = await InsertAsync(new EmploymentContract { Code = Unique("TZ"), WeeklyHours = 20m });
        var marker = Unique("Zz");
        var b = await InsertAsync(new Employee { EmployeeNo = Unique("N"), FirstName = "Berta", LastName = marker, WorkplaceId = await WorkplaceForAsync(location.Id), PlanningRoleId = role.Id, ContractId = contract.Id, RegionCode = "REGION_3" });
        var a = await NewEmployeeAsync(location.Id, marker, "Anna");

        var page = await DataAsync(await GetAsync($"/api/employees?search={marker}", token));

        Assert.Equal([a.Id, b.Id], Ids(page));
        var row = page.GetProperty("items").EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == b.Id);
        Assert.Equal(location.Id, row.GetProperty("locationId").GetGuid());
        Assert.Equal(role.Code, row.GetProperty("planningRoleCode").GetString());
        Assert.Equal(contract.Code, row.GetProperty("contractCode").GetString());
        Assert.Equal(20m, row.GetProperty("weeklyHours").GetDecimal());
        Assert.False(string.IsNullOrEmpty(row.GetProperty("workplaceCode").GetString()));
        Assert.Equal(2, page.GetProperty("meta").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task List_FiltersBySearch_WorkplaceLocationAndActive_AndTakesWildcardsLiterally()
    {
        var token = await AdminTokenAsync();
        var (a, b) = (CreateLocation(), CreateLocation());
        var marker = Unique("Mm");
        var inA = await NewEmployeeAsync(a.Id, marker + "A");
        var inB = await NewEmployeeAsync(b.Id, marker + "B");
        var inactive = await NewEmployeeAsync(a.Id, marker + "C", active: false);

        Assert.Equal([inA.Id, inB.Id], Ids(await DataAsync(await GetAsync($"/api/employees?search={marker}&active=true", token))));
        Assert.Equal([inB.Id], Ids(await DataAsync(await GetAsync($"/api/employees?search={marker}&locationId={b.Id}", token))));
        Assert.Equal([inA.Id], Ids(await DataAsync(await GetAsync($"/api/employees?search={marker}&workplaceId={await WorkplaceForAsync(a.Id)}&active=true", token))));
        Assert.Equal([inactive.Id], Ids(await DataAsync(await GetAsync($"/api/employees?search={marker}&active=false", token))));
        Assert.Equal([inA.Id], Ids(await DataAsync(await GetAsync($"/api/employees?search={inA.EmployeeNo}", token))));   // auch nach Personalnummer
        Assert.Empty(Ids(await DataAsync(await GetAsync("/api/employees?search=%25", token))));                         // „%“ ist kein Platzhalter
    }

    [Fact]
    public async Task ScopedUser_SeesOnlyEmployeesOfTheirLocations_AndGets403ForOthers()
    {
        var (mine, other) = (CreateLocation(), CreateLocation());
        var marker = Unique("Sc");
        var own = await NewEmployeeAsync(mine.Id, marker + "1");
        var foreign = await NewEmployeeAsync(other.Id, marker + "2");
        var withoutWorkplace = await NewEmployeeAsync(null, marker + "3");
        var (_, token) = await ScopedUserAsync(["employee:read", "employee:update", "employee:delete"], [mine.Id]);

        Assert.Equal([own.Id], Ids(await DataAsync(await GetAsync($"/api/employees?search={marker}", token))));
        var detail = await GetAsync($"/api/employees/{foreign.Id}", token);
        Assert.Equal(HttpStatusCode.Forbidden, detail.StatusCode);
        Assert.Equal(AuthMessages.Read, await MessageAsync(detail));
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync($"/api/employees/{withoutWorkplace.Id}", token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync($"/api/employees/{Guid.NewGuid()}", token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PatchAsync($"/api/employees/{foreign.Id}", new { remark = "x" }, token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await DeleteAsync($"/api/employees/{foreign.Id}", token)).StatusCode);
    }

    // --- Pflegen ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Update_ChangesWhatHrMaintains_ButNeverNameNumberOrEmail()
    {
        var token = await AdminTokenAsync();
        var location = CreateLocation();
        var employee = await NewEmployeeAsync(null, "Original");
        var role = await InsertAsync(new PlanningRole { Code = Unique("R").ToUpperInvariant() });
        var contract = await InsertAsync(new EmploymentContract { Code = Unique("TZ"), WeeklyHours = 30m });
        var workplaceId = await WorkplaceForAsync(location.Id);
        var userId = Guid.NewGuid();

        var response = await PatchAsync($"/api/employees/{employee.Id}", new
        {
            workplaceId, planningRoleId = role.Id, contractId = contract.Id, userId, shiftSwapEnabled = true, remark = "Springer", regionCode = "REGION_2",
            lastName = "Geändert", employeeNo = "99999", email = "x@y.de"
        }, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await DataAsync(response);
        Assert.Equal(workplaceId, data.GetProperty("workplaceId").GetGuid());
        Assert.Equal(location.Id, data.GetProperty("locationId").GetGuid());
        Assert.Equal(role.Code, data.GetProperty("planningRoleCode").GetString());
        Assert.Equal(30m, data.GetProperty("weeklyHours").GetDecimal());
        Assert.Equal(userId, data.GetProperty("userId").GetGuid());
        Assert.True(data.GetProperty("shiftSwapEnabled").GetBoolean());
        Assert.Equal("Springer", data.GetProperty("remark").GetString());
        Assert.Equal("REGION_2", data.GetProperty("regionCode").GetString());
        Assert.Equal("Original", data.GetProperty("lastName").GetString());          // kommt aus BC: unverändert
        Assert.Equal(employee.EmployeeNo, data.GetProperty("employeeNo").GetString());
        Assert.Null(data.GetProperty("email").GetString());
    }

    [Fact]
    public async Task Update_WithUnknownReferences_Is422_AndALoginCanBelongToOneEmployeeOnly()
    {
        var token = await AdminTokenAsync();
        var (first, second) = (await NewEmployeeAsync(null), await NewEmployeeAsync(null));
        var userId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await PatchAsync($"/api/employees/{first.Id}", new { userId }, token)).StatusCode);

        var conflict = await PatchAsync($"/api/employees/{second.Id}", new { userId }, token);
        var role = await PatchAsync($"/api/employees/{second.Id}", new { planningRoleId = Guid.NewGuid() }, token);
        var contract = await PatchAsync($"/api/employees/{second.Id}", new { contractId = Guid.NewGuid() }, token);
        var workplace = await PatchAsync($"/api/employees/{second.Id}", new { workplaceId = Guid.NewGuid() }, token);

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.All(new[] { role, contract, workplace }, r => Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode));
        Assert.Equal("planningRoleId", (await JsonAsync(role)).GetProperty("errors")[0].GetProperty("field").GetString());
    }

    [Fact]
    public async Task Update_AScopedUserCannotMoveAnEmployeeOutOfTheirScope_OrToAWorkplaceWithoutLocation()
    {
        var (mine, other) = (CreateLocation(), CreateLocation());
        var employee = await NewEmployeeAsync(mine.Id);
        var (_, token) = await ScopedUserAsync(["employee:update", "employee:read"], [mine.Id]);

        var away = await PatchAsync($"/api/employees/{employee.Id}", new { workplaceId = await WorkplaceForAsync(other.Id) }, token);
        var noLocation = await PatchAsync($"/api/employees/{employee.Id}", new { workplaceId = (await WorkplaceWithoutLocationAsync()).Id }, token);

        Assert.Equal(HttpStatusCode.Forbidden, away.StatusCode);
        Assert.Equal(AuthMessages.Update, await MessageAsync(away));
        Assert.Equal(HttpStatusCode.Forbidden, noLocation.StatusCode);
        Assert.Equal(await WorkplaceForAsync(mine.Id), (await Factory.WithDbAsync((db, _) => db.Employees.SingleAsync(e => e.Id == employee.Id))).WorkplaceId);
    }

    [Fact]
    public async Task Delete_Deactivates_IsRepeatable_AndPatchCanActivateAgain()
    {
        var token = await AdminTokenAsync();
        var employee = await NewEmployeeAsync(null);

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync($"/api/employees/{employee.Id}", token)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync($"/api/employees/{employee.Id}", token)).StatusCode);
        Assert.False((await DataAsync(await GetAsync($"/api/employees/{employee.Id}", token))).GetProperty("active").GetBoolean());

        var again = await PatchAsync($"/api/employees/{employee.Id}", new { active = true }, token);
        Assert.True((await DataAsync(again)).GetProperty("active").GetBoolean());
    }

    [Fact]
    public async Task WithoutTheRightPrivilege_TheRoutesAreForbidden()
    {
        var employee = await NewEmployeeAsync(null);
        var (_, readOnly) = await UserWithPrivilegesAsync("employee:read");

        Assert.Equal(HttpStatusCode.OK, (await GetAsync($"/api/employees/{employee.Id}", readOnly)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PatchAsync($"/api/employees/{employee.Id}", new { remark = "x" }, readOnly)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await DeleteAsync($"/api/employees/{employee.Id}", readOnly)).StatusCode);
    }
}
