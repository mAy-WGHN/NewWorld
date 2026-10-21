using HrTool_API.Authentication;
using HrTool_API.Common.Api;
using HrTool_API.Contracts;
using HrTool_API.Data;
using HrTool_API.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrTool_API.Workforce;

/// <summary>
/// Nicht-Verfügbarkeiten der Mitarbeiter. Alle Arten pflegt HR und sie gelten sofort als genehmigt; <b>Genehmigungsstatus und Ablehnungsgrund gibt es nur bei Arten mit
/// <c>requiresApproval</c></b> (Urlaubsanträge: Mitarbeiter beantragt, Vorgesetzter akzeptiert, HR entscheidet). Eigene Rechte für das Genehmigen gibt es bewusst noch nicht:
/// Es ist ein <c>absence:update</c> (siehe CLAUDE.md).
/// </summary>
[Route("api/absences")]
public class AbsencesController(HrToolDbContext db) : ApiControllerBase
{
    private const string NoApproval = "Nur Urlaubsanträge werden genehmigt.";

    [HttpGet, HasPrivilege<Absence>(CrudAction.Read)]
    public async Task<IActionResult> List([FromQuery] AbsenceFilterRequest filter, CancellationToken ct)
    {
        var query = db.Absences.AsQueryable();
        if (filter.EmployeeId is { } employeeId) query = query.Where(a => a.EmployeeId == employeeId);
        if (filter.AbsenceTypeId is { } typeId) query = query.Where(a => a.AbsenceTypeId == typeId);
        if (filter.WorkplaceId is { } workplaceId) query = query.Where(a => a.Employee!.WorkplaceId == workplaceId);
        if (filter.LocationId is { } locationId) query = query.Where(a => a.Employee!.Workplace!.LocationId == locationId);
        if (filter.From is { } from) query = query.Where(a => a.DateTo >= from);
        if (filter.To is { } to) query = query.Where(a => a.DateFrom <= to);
        if (!string.IsNullOrWhiteSpace(filter.ApprovalStatus)) query = query.Where(a => a.ApprovalStatus == filter.ApprovalStatus.Trim());

        var desc = filter.IsDescending(defaultDescending: true);
        var ordered = query.OrderByDirection(a => a.DateFrom, desc).ThenBy(a => a.Employee!.LastName).ThenBy(a => a.Id);
        return Success(await ordered.Select(AbsenceResponse.Projection).ToPagedAsync(filter.Offset, filter.Limit, ct));
    }

    [HttpGet("{id:guid}"), HasPrivilege<Absence>(CrudAction.Read)]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct) =>
        Success(await db.Absences.Where(a => a.Id == id).Select(AbsenceResponse.Projection).FirstOrDefaultAsync(ct) ?? throw await MissingAsync(id, CrudAction.Read, ct));

    [HttpPost, HasPrivilege<Absence>(CrudAction.Create)]
    public async Task<IActionResult> Create(CreateAbsenceRequest request, CancellationToken ct)
    {
        var employeeId = request.EmployeeId!.Value;
        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, ct)
                       ?? throw (await db.Employees.IgnoreQueryFilters().AnyAsync(e => e.Id == employeeId, ct) ? AuthMessages.Forbidden(CrudAction.Create) : ApiException.NotFound("Der Mitarbeiter wurde nicht gefunden."));
        if (!employee.Active) throw ApiException.NotFound("Der Mitarbeiter wurde nicht gefunden oder ist deaktiviert.");

        var type = await db.AbsenceTypes.FirstOrDefaultAsync(t => t.Id == request.AbsenceTypeId, ct)
                   ?? throw ApiException.Validation("absenceTypeId", "Die Nichtverfügbarkeitsart wurde nicht gefunden.");

        Validate(request.DateFrom!.Value, request.DateTo!.Value, request.TimeFrom, request.TimeTo);
        if (!type.RequiresApproval && request.ApprovalStatus is not null) throw ApiException.Validation("approvalStatus", NoApproval);

        var absence = new Absence
        {
            EmployeeId = employee.Id,
            AbsenceTypeId = type.Id,
            DateFrom = request.DateFrom.Value,
            DateTo = request.DateTo.Value,
            Days = request.Days ?? 0,
            VacationDays = request.VacationDays ?? 0,
            TimeFrom = request.TimeFrom,
            TimeTo = request.TimeTo,
            Remark = request.Remark,
            ApprovalStatus = request.ApprovalStatus?.Trim(),
            // Ein Urlaubsantrag trägt den Zeitpunkt, an dem er gestellt wurde.
            RequestedAt = request.RequestedAt ?? (type.RequiresApproval ? DateTime.UtcNow : null),
            Confirmed = request.Confirmed,
            RecordedBy = User.FindFirst("username")?.Value
        };
        db.Absences.Add(absence);
        await db.SaveChangesAsync(ct);
        return CreatedData(await db.Absences.Where(a => a.Id == absence.Id).Select(AbsenceResponse.Projection).FirstAsync(ct));
    }

    [HttpPatch("{id:guid}"), HasPrivilege<Absence>(CrudAction.Update)]
    public async Task<IActionResult> Update(Guid id, UpdateAbsenceRequest request, CancellationToken ct)
    {
        // FindAsync wendet keine Abfragefilter an: bewusst die gefilterte Abfrage.
        var absence = await db.Absences.Include(a => a.AbsenceType).FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw await MissingAsync(id, CrudAction.Update, ct);

        var from = request.DateFrom ?? absence.DateFrom;
        var to = request.DateTo ?? absence.DateTo;
        var timeFrom = request.TimeFrom ?? absence.TimeFrom;
        var timeTo = request.TimeTo ?? absence.TimeTo;
        Validate(from, to, timeFrom, timeTo);

        if (!absence.AbsenceType!.RequiresApproval && (request.ApprovalStatus is not null || request.RejectionReason is not null))
            throw ApiException.Validation(request.ApprovalStatus is not null ? "approvalStatus" : "rejectionReason", NoApproval);

        absence.DateFrom = from;
        absence.DateTo = to;
        absence.TimeFrom = timeFrom;
        absence.TimeTo = timeTo;
        if (request.Days is not null) absence.Days = request.Days.Value;
        if (request.VacationDays is not null) absence.VacationDays = request.VacationDays.Value;
        if (request.Remark is not null) absence.Remark = request.Remark;
        if (request.ApprovalStatus is not null) absence.ApprovalStatus = request.ApprovalStatus.Trim();
        if (request.RejectionReason is not null) absence.RejectionReason = request.RejectionReason;
        if (request.Confirmed is not null) absence.Confirmed = request.Confirmed.Value;

        await db.SaveChangesAsync(ct);
        return Success(await db.Absences.Where(a => a.Id == id).Select(AbsenceResponse.Projection).FirstAsync(ct));
    }

    [HttpDelete("{id:guid}"), HasPrivilege<Absence>(CrudAction.Delete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var absence = await db.Absences.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw await MissingAsync(id, CrudAction.Delete, ct);
        db.Absences.Remove(absence);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static void Validate(DateOnly from, DateOnly to, TimeOnly? timeFrom, TimeOnly? timeTo)
    {
        if (to < from) throw ApiException.Validation("dateTo", "Das Ende darf nicht vor dem Beginn liegen.");
        if (timeFrom is null != timeTo is null)
            throw ApiException.Validation(timeFrom is null ? "timeFrom" : "timeTo", "Dieser Wert darf nicht leer sein. Beginn und Ende gelten nur zusammen.");
    }

    // Außerhalb des eigenen Scopes: 403 mit der vordefinierten Meldung (die Abwesenheit existiert); nicht vorhanden: 404.
    private async Task<ApiException> MissingAsync(Guid id, CrudAction action, CancellationToken ct) =>
        await db.Absences.IgnoreQueryFilters().AnyAsync(a => a.Id == id, ct) ? AuthMessages.Forbidden(action) : ApiException.NotFound();
}
