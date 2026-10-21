using HrTool_API.Authentication;
using HrTool_API.Common.Api;
using HrTool_API.Contracts;
using HrTool_API.Data;
using HrTool_API.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HrTool_API.Hr;

/// <summary>
/// Mitarbeiter der Personalplanung. Die Stammdaten kommen aus BC (Import); hier liest man sie und pflegt, was HR selbst verantwortet
/// (Arbeitsort, Rolle, Vertrag, Login, Schichttausch, Bemerkung). Löschen heißt deaktivieren.
/// </summary>
[Route("api/employees")]
public class EmployeesController(HrToolDbContext db, ScopeContext scope) : ApiControllerBase
{
    [HttpGet, HasPrivilege<Employee>(CrudAction.Read)]
    public async Task<IActionResult> List([FromQuery] EmployeeFilterRequest filter, CancellationToken ct)
    {
        var query = db.Employees.AsQueryable();
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var pattern = $"%{EscapeLike(filter.Search.Trim())}%";
            query = query.Where(e => EF.Functions.ILike(e.EmployeeNo, pattern, "\\")
                                     || EF.Functions.ILike(e.FirstName + " " + e.LastName, pattern, "\\")
                                     || EF.Functions.ILike(e.LastName + " " + e.FirstName, pattern, "\\"));
        }

        if (filter.WorkplaceId is { } workplaceId) query = query.Where(e => e.WorkplaceId == workplaceId);
        if (filter.LocationId is { } locationId) query = query.Where(e => e.Workplace!.LocationId == locationId);
        if (filter.PlanningRoleId is { } roleId) query = query.Where(e => e.PlanningRoleId == roleId);
        if (filter.Active is { } active) query = query.Where(e => e.Active == active);

        var desc = filter.IsDescending(defaultDescending: false);
        var ordered = query.OrderByDirection(e => e.LastName, desc).ThenByDirection(e => e.FirstName, desc).ThenBy(e => e.EmployeeNo);
        return Success(await ordered.Select(EmployeeResponse.Projection).ToPagedAsync(filter.Offset, filter.Limit, ct));
    }

    [HttpGet("{id:guid}"), HasPrivilege<Employee>(CrudAction.Read)]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct) =>
        Success(await db.Employees.Where(e => e.Id == id).Select(EmployeeResponse.Projection).FirstOrDefaultAsync(ct)
                ?? throw await MissingAsync(id, CrudAction.Read, ct));

    [HttpPatch("{id:guid}"), HasPrivilege<Employee>(CrudAction.Update)]
    public async Task<IActionResult> Update(Guid id, UpdateEmployeeRequest request, CancellationToken ct)
    {
        // FindAsync wendet keine Abfragefilter an: bewusst die gefilterte Abfrage.
        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == id, ct) ?? throw await MissingAsync(id, CrudAction.Update, ct);

        if (request.WorkplaceId is { } workplaceId)
        {
            var workplace = await db.Workplaces.FirstOrDefaultAsync(w => w.Id == workplaceId, ct)
                            ?? throw ApiException.Validation("workplaceId", "Der Arbeitsort wurde nicht gefunden.");
            // Eingeschränkt gibt man Mitarbeiter nur an Arbeitsorte der eigenen Filialen (nicht aus dem eigenen Scope heraus).
            if (!scope.Current.All && !(workplace.LocationId is { } inScope && scope.Current.AllowsLocation(inScope))) throw AuthMessages.Forbidden(CrudAction.Update);
            employee.WorkplaceId = workplace.Id;
        }

        if (request.PlanningRoleId is { } roleId)
        {
            if (!await db.PlanningRoles.AnyAsync(r => r.Id == roleId, ct)) throw ApiException.Validation("planningRoleId", "Die Rolle wurde nicht gefunden.");
            employee.PlanningRoleId = roleId;
        }

        if (request.ContractId is { } contractId)
        {
            if (!await db.EmploymentContracts.AnyAsync(c => c.Id == contractId, ct)) throw ApiException.Validation("contractId", "Das Beschäftigungsverhältnis wurde nicht gefunden.");
            employee.ContractId = contractId;
        }

        if (request.UserId is { } userId && userId != employee.UserId)
        {
            if (await db.Employees.IgnoreQueryFilters().AnyAsync(e => e.UserId == userId && e.Id != id, ct))
                throw ApiException.Conflict("Dieser Login gehört bereits zu einem anderen Mitarbeiter.");
            employee.UserId = userId;
        }

        if (request.RegionCode is not null) employee.RegionCode = request.RegionCode.Trim();
        if (request.ShiftSwapEnabled is not null) employee.ShiftSwapEnabled = request.ShiftSwapEnabled.Value;
        if (request.Remark is not null) employee.Remark = request.Remark;
        if (request.Active is not null) employee.Active = request.Active.Value;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw ApiException.Conflict("Dieser Login gehört bereits zu einem anderen Mitarbeiter.");
        }

        return Success(await db.Employees.Where(e => e.Id == id).Select(EmployeeResponse.Projection).FirstAsync(ct));
    }

    // Deaktivieren statt Löschen: Pläne, Schichten und Abwesenheiten verweisen auf den Mitarbeiter. Wiederholtes Aufrufen ist idempotent.
    [HttpDelete("{id:guid}"), HasPrivilege<Employee>(CrudAction.Delete)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == id, ct) ?? throw await MissingAsync(id, CrudAction.Delete, ct);
        employee.Active = false;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // Außerhalb des eigenen Scopes: 403 mit der vordefinierten Meldung (der Mitarbeiter existiert); nicht vorhanden: 404.
    private async Task<ApiException> MissingAsync(Guid id, CrudAction action, CancellationToken ct) =>
        await db.Employees.IgnoreQueryFilters().AnyAsync(e => e.Id == id, ct) ? AuthMessages.Forbidden(action) : ApiException.NotFound();

    private static string EscapeLike(string text) => text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
