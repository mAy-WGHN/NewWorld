using HrTool_API.Authentication;
using HrTool_API.Common.Api;
using HrTool_API.Contracts;
using HrTool_API.Data;
using HrTool_API.Domain.Entities;
using HrTool_API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HrTool_API.Workforce;

/// <summary>
/// Dienstpläne (je Filiale und Monat) und ihre Schichten. Schichten haben kein eigenes Recht: Lesen verlangt <c>schedule:read</c>, jede Änderung <c>schedule:update</c>.
/// </summary>
[Route("api/schedules")]
public class SchedulesController(HrToolDbContext db, ILocationDirectory locationDirectory, ScopeContext scope) : ApiControllerBase
{
    private const string WorkplaceNotFound = "Der Arbeitsort wurde nicht gefunden.";
    private const string LocationNotFound = "Die Filiale wurde nicht gefunden oder ist archiviert.";
    private const string Required = "Dieser Wert darf nicht leer sein.";

    // --- Pläne ----------------------------------------------------------------------------------------------

    [HttpGet, HasPrivilege<Schedule>(CrudAction.Read)]
    public async Task<IActionResult> List([FromQuery] ScheduleFilterRequest filter, CancellationToken ct)
    {
        var query = db.Schedules.AsQueryable();
        if (filter.WorkplaceId is { } workplaceId) query = query.Where(s => s.WorkplaceId == workplaceId);
        if (filter.LocationId is { } locationId) query = query.Where(s => s.Workplace!.LocationId == locationId);
        if (filter.Year is { } year) query = query.Where(s => s.Year == year);
        if (filter.Month is { } month) query = query.Where(s => s.Month == month);
        if (filter.Status is { } status) query = query.Where(s => s.Status == status);

        var desc = filter.IsDescending(defaultDescending: true);
        var ordered = query.OrderByDirection(s => s.Year, desc).ThenByDirection(s => s.Month, desc).ThenBy(s => s.Number);
        return Success(await ordered.Select(ScheduleResponse.Projection).ToPagedAsync(filter.Offset, filter.Limit, ct));
    }

    [HttpGet("{id:guid}"), HasPrivilege<Schedule>(CrudAction.Read)]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct)
    {
        var schedule = await db.Schedules.Include(s => s.Workplace).FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw await MissingAsync(id, CrudAction.Read, ct);
        return Success(await DetailAsync(schedule, ct));
    }

    [HttpPost, HasPrivilege<Schedule>(CrudAction.Create)]
    public async Task<IActionResult> Create(CreateScheduleRequest request, CancellationToken ct)
    {
        var workplace = await db.Workplaces.FirstOrDefaultAsync(w => w.Id == request.WorkplaceId, ct) ?? throw ApiException.NotFound(WorkplaceNotFound);

        // Eingeschränkt legt man nur Pläne für Arbeitsorte an, deren Filiale im eigenen Scope liegt; Arbeitsorte ohne Filiale (BS, SABA) nur, wer uneingeschränkt darf.
        if (!scope.Current.All && !(workplace.LocationId is { } inScope && scope.Current.AllowsLocation(inScope))) throw AuthMessages.Forbidden(CrudAction.Create);
        if (workplace.LocationId is { } locationId) await EnsureLocationUsableAsync(locationId, ct);

        var number = request.Number!.Trim();
        if (await db.Schedules.IgnoreQueryFilters().AnyAsync(s => s.WorkplaceId == workplace.Id && s.Year == request.Year && s.Month == request.Month, ct))
            throw ApiException.Conflict("Für diesen Arbeitsort und diesen Monat gibt es bereits einen Dienstplan.");
        if (await db.Schedules.IgnoreQueryFilters().AnyAsync(s => s.Number == number, ct))
            throw ApiException.Conflict("Diese Plannummer ist bereits vergeben.");

        var schedule = new Schedule
        {
            Number = number,
            WorkplaceId = workplace.Id,
            Workplace = workplace,
            Year = request.Year!.Value,
            Month = request.Month!.Value,
            Status = request.Status ?? ScheduleStatus.Draft,
            BudgetHours = request.BudgetHours ?? 0
        };
        db.Schedules.Add(schedule);
        await SaveAsync(ct);
        return CreatedData(await DetailAsync(schedule, ct));
    }

    [HttpPatch("{id:guid}"), HasPrivilege<Schedule>(CrudAction.Update)]
    public async Task<IActionResult> Update(Guid id, UpdateScheduleRequest request, CancellationToken ct)
    {
        // FindAsync wendet keine Abfragefilter an: bewusst die gefilterte Abfrage.
        var schedule = await db.Schedules.Include(s => s.Workplace).FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw await MissingAsync(id, CrudAction.Update, ct);

        if (request.Status is not null) schedule.Status = request.Status.Value;
        if (request.BudgetHours is not null) schedule.BudgetHours = request.BudgetHours.Value;

        await db.SaveChangesAsync(ct);
        return Success(await DetailAsync(schedule, ct));
    }

    // Bestätigte Pläne sind Grundlage von Auszahlung und Benachrichtigung: Sie lassen sich nur löschen, nachdem sie wieder auf Entwurf stehen.
    [HttpDelete("{id:guid}"), HasPrivilege<Schedule>(CrudAction.Delete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var schedule = await db.Schedules.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw await MissingAsync(id, CrudAction.Delete, ct);
        if (schedule.Status != ScheduleStatus.Draft)
            throw ApiException.Conflict("Nur Dienstpläne im Entwurf lassen sich löschen.");

        db.Schedules.Remove(schedule); // die Schichten gehen per Cascade mit
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // --- Matrix ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Der Dienstplan als Matrix (wie die BC-Ansicht): der Monat samt Rand, je Mitarbeiter seine Schichten, Soll, Geplant und Geplant %.
    /// Die Schichten im Rand gehören zu Nachbarplänen desselben Arbeitsorts; Summen zählen nur den Monat des Plans.
    /// </summary>
    [HttpGet("{id:guid}/matrix"), HasPrivilege<Schedule>(CrudAction.Read)]
    public async Task<IActionResult> Matrix(Guid id, [FromQuery] MatrixRequest request, CancellationToken ct)
    {
        var schedule = await db.Schedules.Include(s => s.Workplace).FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw await MissingAsync(id, CrudAction.Read, ct);
        var firstDay = new DateOnly(schedule.Year, schedule.Month, 1);
        var lastDay = firstDay.AddMonths(1).AddDays(-1);
        var from = firstDay.AddDays(-request.MarginDays);
        var to = lastDay.AddDays(request.MarginDays);

        // Die Pläne desselben Arbeitsorts liegen im Scope des Plans: der Abfragefilter lässt sie durch.
        var shifts = await db.Shifts
            .Where(s => s.Schedule!.WorkplaceId == schedule.WorkplaceId && s.Date >= from && s.Date <= to)
            .OrderBy(s => s.Date).ThenBy(s => s.StartTime)
            .ToListAsync(ct);

        // Mitarbeiter des Arbeitsorts (auch ohne Schichten, wie in BC) ...
        var home = await db.Employees.Include(e => e.Contract)
            .Where(e => e.WorkplaceId == schedule.WorkplaceId && e.Active)
            .OrderBy(e => e.LastName).ThenBy(e => e.FirstName).ThenBy(e => e.EmployeeNo)
            .ToListAsync(ct);
        var homeIds = home.Select(e => e.Id).ToHashSet();

        // ... und Mitarbeiter anderer Arbeitsorte, die in diesem Plan eingeteilt sind. Sie sind Teil dieses Plans; ihr Arbeitsort und ihr Scope spielen hier keine Rolle.
        var visitingIds = shifts.Select(s => s.EmployeeId).Where(e => !homeIds.Contains(e)).Distinct().ToList();
        var visiting = visitingIds.Count == 0
            ? []
            : await db.Employees.IgnoreQueryFilters().Where(e => visitingIds.Contains(e.Id))
                .OrderBy(e => e.LastName).ThenBy(e => e.FirstName).ThenBy(e => e.EmployeeNo).ToListAsync(ct);

        // Eingesetzt in anderen Filialen („YY …“ in BC): Schichten der eigenen Mitarbeiter in Plänen anderer Arbeitsorte. Ohne Scope-Filter, aber nur Datum, Zeit und Arbeitsort.
        var away = await db.Shifts.IgnoreQueryFilters()
            .Where(s => homeIds.Contains(s.EmployeeId) && s.Schedule!.WorkplaceId != schedule.WorkplaceId && s.Date >= from && s.Date <= to)
            .Select(s => new { s.EmployeeId, s.Date, s.StartTime, s.EndTime, s.Schedule!.WorkplaceId, WorkplaceCode = s.Schedule.Workplace!.Code })
            .ToListAsync(ct);

        var typeIds = shifts.Where(s => s.AbsenceTypeId != null).Select(s => s.AbsenceTypeId!.Value).Distinct().ToList();
        var types = await db.AbsenceTypes.Where(t => typeIds.Contains(t.Id)).OrderBy(t => t.Code).ToListAsync(ct);

        var days = DateTime.DaysInMonth(schedule.Year, schedule.Month);
        MatrixRow BuildRow(Employee e, bool isHome)
        {
            var mine = shifts.Where(s => s.EmployeeId == e.Id).Select(s =>
                new MatrixShift(s.Id, s.Date, s.StartTime, s.EndTime, s.ShiftCode, s.PlanningRoleId, s.AbsenceTypeId, s.Remark, ScheduleCalculator.CountedHours(s.StartTime, s.EndTime))).ToList();
            var planned = mine.Where(s => s.Date >= firstDay && s.Date <= lastDay).Sum(s => s.Hours);
            decimal? target = isHome && e.Contract?.WeeklyHours is { } weekly ? ScheduleCalculator.TargetHours(weekly, days) : null;
            var elsewhere = away.Where(a => a.EmployeeId == e.Id).OrderBy(a => a.Date).ThenBy(a => a.StartTime)
                .Select(a => new MatrixAway(a.Date, a.StartTime, a.EndTime, a.WorkplaceId, a.WorkplaceCode)).ToList();
            return new MatrixRow(e.Id, e.EmployeeNo, e.FirstName, e.LastName, isHome, target, planned, ScheduleCalculator.PlannedPercent(planned, target), mine, elsewhere);
        }

        var rows = home.Select(e => BuildRow(e, true)).Concat(visiting.Select(e => BuildRow(e, false))).ToList();
        var detail = ScheduleDetailResponse.From(schedule, rows.Sum(r => r.Shifts.Where(s => s.Date >= firstDay && s.Date <= lastDay).Sum(s => s.Hours)));
        return Success(new MatrixResponse(detail, from, to,
            types.Select(t => new MatrixAbsenceType(t.Id, t.Code, t.ScheduleSymbol, t.PaidTime)).ToList(), rows));
    }

    // --- Schichten ------------------------------------------------------------------------------------------

    [HttpGet("{id:guid}/shifts"), HasPrivilege<Schedule>(CrudAction.Read)]
    public async Task<IActionResult> Shifts(Guid id, [FromQuery] ShiftFilterRequest filter, CancellationToken ct)
    {
        if (!await db.Schedules.AnyAsync(s => s.Id == id, ct)) throw await MissingAsync(id, CrudAction.Read, ct);

        var query = db.Shifts.Where(s => s.ScheduleId == id);
        if (filter.EmployeeId is { } employeeId) query = query.Where(s => s.EmployeeId == employeeId);
        if (filter.From is { } from) query = query.Where(s => s.Date >= from);
        if (filter.To is { } to) query = query.Where(s => s.Date <= to);

        // Ein Monatsplan hat höchstens ein paar Hundert Schichten: ohne Paging, sortiert wie die Matrix (Tag, Beginn).
        return Success(await query.OrderBy(s => s.Date).ThenBy(s => s.StartTime).ThenBy(s => s.EmployeeId)
            .Select(ShiftResponse.Projection).ToListAsync(ct));
    }

    [HttpPost("{id:guid}/shifts"), HasPrivilege<Schedule>(CrudAction.Update)]
    public async Task<IActionResult> CreateShift(Guid id, CreateShiftRequest request, CancellationToken ct)
    {
        var schedule = await db.Schedules.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw await MissingAsync(id, CrudAction.Update, ct);

        var employeeId = request.EmployeeId!.Value;
        // Nur auf Existenz prüfen (ohne Scope): Auch Mitarbeiter anderer Filialen können einspringen. Daten des Mitarbeiters gibt diese Route nicht preis.
        if (!await db.Employees.IgnoreQueryFilters().AnyAsync(e => e.Id == employeeId && e.Active, ct))
            throw ApiException.NotFound("Der Mitarbeiter wurde nicht gefunden oder ist deaktiviert.");

        ValidateShift(schedule, request.Date!.Value, request.StartTime, request.EndTime);
        await EnsureReferencesAsync(request.PlanningRoleId, request.AbsenceTypeId, ct);

        var shift = new Shift
        {
            ScheduleId = id,
            EmployeeId = employeeId,
            Date = request.Date.Value,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            ShiftCode = request.ShiftCode?.Trim(),
            PlanningRoleId = request.PlanningRoleId,
            AbsenceTypeId = request.AbsenceTypeId,
            Remark = request.Remark
        };
        db.Shifts.Add(shift);
        await db.SaveChangesAsync(ct);
        return CreatedData(ShiftResponse.From(shift));
    }

    [HttpPatch("{id:guid}/shifts/{shiftId:guid}"), HasPrivilege<Schedule>(CrudAction.Update)]
    public async Task<IActionResult> UpdateShift(Guid id, Guid shiftId, UpdateShiftRequest request, CancellationToken ct)
    {
        var schedule = await db.Schedules.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw await MissingAsync(id, CrudAction.Update, ct);
        var shift = await db.Shifts.FirstOrDefaultAsync(s => s.Id == shiftId && s.ScheduleId == id, ct) ?? throw ApiException.NotFound();

        var date = request.Date ?? shift.Date;
        var start = request.StartTime ?? shift.StartTime;
        var end = request.EndTime ?? shift.EndTime;
        ValidateShift(schedule, date, start, end);
        await EnsureReferencesAsync(request.PlanningRoleId, request.AbsenceTypeId, ct);

        shift.Date = date;
        shift.StartTime = start;
        shift.EndTime = end;
        if (request.ShiftCode is not null) shift.ShiftCode = request.ShiftCode.Trim();
        if (request.PlanningRoleId is not null) shift.PlanningRoleId = request.PlanningRoleId;
        if (request.AbsenceTypeId is not null) shift.AbsenceTypeId = request.AbsenceTypeId;
        if (request.Remark is not null) shift.Remark = request.Remark;

        await db.SaveChangesAsync(ct);
        return Success(ShiftResponse.From(shift));
    }

    [HttpDelete("{id:guid}/shifts/{shiftId:guid}"), HasPrivilege<Schedule>(CrudAction.Update)]
    public async Task<IActionResult> DeleteShift(Guid id, Guid shiftId, CancellationToken ct)
    {
        if (!await db.Schedules.AnyAsync(s => s.Id == id, ct)) throw await MissingAsync(id, CrudAction.Update, ct);
        var shift = await db.Shifts.FirstOrDefaultAsync(s => s.Id == shiftId && s.ScheduleId == id, ct) ?? throw ApiException.NotFound();

        db.Shifts.Remove(shift);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // --- Hilfen ---------------------------------------------------------------------------------------------

    // Geplante Stunden eines Plans: nur Schichten im Monat des Plans, nur die Zeiten werden geladen.
    private async Task<ScheduleDetailResponse> DetailAsync(Schedule schedule, CancellationToken ct)
    {
        var times = await db.Shifts.Where(s => s.ScheduleId == schedule.Id).Select(s => new { s.Date, s.StartTime, s.EndTime }).ToListAsync(ct);
        var planned = times.Where(s => s.Date.Year == schedule.Year && s.Date.Month == schedule.Month).Sum(s => ScheduleCalculator.CountedHours(s.StartTime, s.EndTime));
        return ScheduleDetailResponse.From(schedule, planned);
    }

    // Außerhalb des eigenen Scopes: 403 mit der vordefinierten Meldung (der Plan existiert); nicht vorhanden: 404.
    private async Task<ApiException> MissingAsync(Guid id, CrudAction action, CancellationToken ct) =>
        await db.Schedules.IgnoreQueryFilters().AnyAsync(s => s.Id == id, ct) ? AuthMessages.Forbidden(action) : ApiException.NotFound();

    private static void ValidateShift(Schedule schedule, DateOnly date, TimeOnly? start, TimeOnly? end)
    {
        if (date.Year != schedule.Year || date.Month != schedule.Month)
            throw ApiException.Validation("date", "Das Datum muss im Monat des Dienstplans liegen.");
        if (start is null != end is null)
            throw ApiException.Validation(start is null ? "startTime" : "endTime", Required + " Beginn und Ende gelten nur zusammen.");
    }

    private async Task EnsureReferencesAsync(Guid? planningRoleId, Guid? absenceTypeId, CancellationToken ct)
    {
        if (planningRoleId is { } roleId && !await db.PlanningRoles.AnyAsync(r => r.Id == roleId, ct))
            throw ApiException.Validation("planningRoleId", "Die Rolle wurde nicht gefunden.");
        if (absenceTypeId is { } typeId && !await db.AbsenceTypes.AnyAsync(t => t.Id == typeId, ct))
            throw ApiException.Validation("absenceTypeId", "Die Nichtverfügbarkeitsart wurde nicht gefunden.");
    }

    // Eine unbekannte Filiale kann gerade erst angelegt worden sein: vor dem 404 einmal frisch nachsehen.
    private async Task EnsureLocationUsableAsync(Guid locationId, CancellationToken ct)
    {
        var known = await locationDirectory.GetAllAsync(ct);
        if (!IsUsable(known, locationId)) known = await locationDirectory.GetAllAsync(ct, forceRefresh: true);
        if (!IsUsable(known, locationId)) throw ApiException.NotFound(LocationNotFound);
    }

    private static bool IsUsable(IReadOnlyDictionary<Guid, LocationResponse> known, Guid id) => known.TryGetValue(id, out var location) && !location.Archived;

    // Zwei gleichzeitige Anlagen: Die Vorabprüfung sieht die andere noch nicht, die eindeutigen Indizes der DB fangen es ab.
    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw ApiException.Conflict("Für diese Filiale und diesen Monat gibt es bereits einen Dienstplan, oder die Plannummer ist vergeben.");
        }
    }
}
