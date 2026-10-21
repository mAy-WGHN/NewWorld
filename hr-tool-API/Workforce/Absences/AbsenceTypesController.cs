using HrTool_API.Authentication;
using HrTool_API.Common.Api;
using HrTool_API.Contracts;
using HrTool_API.Data;
using HrTool_API.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HrTool_API.Workforce;

/// <summary>Nichtverfügbarkeitsarten (Urlaub, Krank …). Die 24 Arten aus BC legt der Dienst beim Start an; hier pflegt man sie weiter. Genehmigt werden muss nur, was <c>requiresApproval</c> trägt (Urlaub).</summary>
[Route("api/absence-types")]
public class AbsenceTypesController(HrToolDbContext db) : ApiControllerBase
{
    [HttpGet, HasPrivilege<AbsenceType>(CrudAction.Read)]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Success(await db.AbsenceTypes.OrderBy(t => t.Code).Select(AbsenceTypeResponse.Projection).ToListAsync(ct));

    [HttpGet("{id:guid}"), HasPrivilege<AbsenceType>(CrudAction.Read)]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct) =>
        Success(await db.AbsenceTypes.Where(t => t.Id == id).Select(AbsenceTypeResponse.Projection).FirstOrDefaultAsync(ct) ?? throw ApiException.NotFound());

    [HttpPost, HasPrivilege<AbsenceType>(CrudAction.Create)]
    public async Task<IActionResult> Create(CreateAbsenceTypeRequest request, CancellationToken ct)
    {
        var code = request.Code!.Trim().ToUpperInvariant();
        if (await db.AbsenceTypes.AnyAsync(t => t.Code == code, ct)) throw ApiException.Conflict("Dieser Code ist bereits vergeben.");

        var type = new AbsenceType
        {
            Code = code,
            Description = request.Description!.Trim(),
            PaidTime = request.PaidTime,
            ScheduleSymbol = Clean(request.ScheduleSymbol),
            SetWorkCode = Clean(request.SetWorkCode),
            RequiresApproval = request.RequiresApproval
        };
        db.AbsenceTypes.Add(type);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw ApiException.Conflict("Dieser Code ist bereits vergeben.");
        }

        return CreatedData(await db.AbsenceTypes.Where(t => t.Id == type.Id).Select(AbsenceTypeResponse.Projection).FirstAsync(ct));
    }

    [HttpPatch("{id:guid}"), HasPrivilege<AbsenceType>(CrudAction.Update)]
    public async Task<IActionResult> Update(Guid id, UpdateAbsenceTypeRequest request, CancellationToken ct)
    {
        var type = await db.AbsenceTypes.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw ApiException.NotFound();

        if (request.Description is not null) type.Description = request.Description.Trim();
        if (request.PaidTime is not null) type.PaidTime = request.PaidTime.Value;
        if (request.ScheduleSymbol is not null) type.ScheduleSymbol = Clean(request.ScheduleSymbol);
        if (request.SetWorkCode is not null) type.SetWorkCode = Clean(request.SetWorkCode);
        if (request.RequiresApproval is not null) type.RequiresApproval = request.RequiresApproval.Value;

        await db.SaveChangesAsync(ct);
        return Success(await db.AbsenceTypes.Where(t => t.Id == id).Select(AbsenceTypeResponse.Projection).FirstAsync(ct));
    }

    // Eine verwendete Art lässt sich nicht löschen: Abwesenheiten und Schichten verweisen darauf.
    [HttpDelete("{id:guid}"), HasPrivilege<AbsenceType>(CrudAction.Delete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var type = await db.AbsenceTypes.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw ApiException.NotFound();
        // Ohne Scope-Filter: Auch Verwendungen in Filialen, die der Nutzer nicht sieht, zählen.
        if (await db.Absences.IgnoreQueryFilters().AnyAsync(a => a.AbsenceTypeId == id, ct) || await db.Shifts.IgnoreQueryFilters().AnyAsync(s => s.AbsenceTypeId == id, ct))
            throw ApiException.Conflict("Die Art wird verwendet und lässt sich nicht löschen.");

        db.AbsenceTypes.Remove(type);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
