using HrTool_API.Authentication;
using HrTool_API.Common.Api;
using HrTool_API.Contracts;
using HrTool_API.Data;
using HrTool_API.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HrTool_API.Workforce;

/// <summary>Schichtmuster (Kopfdaten: Code, Beschreibung, Tages-/Wochenmuster, Länge in Wochen). Die Tage und Zeiten der Muster sind noch nicht modelliert (nicht im Export).</summary>
[Route("api/shift-patterns")]
public class ShiftPatternsController(HrToolDbContext db) : ApiControllerBase
{
    [HttpGet, HasPrivilege<ShiftPattern>(CrudAction.Read)]
    public async Task<IActionResult> List([FromQuery] ShiftPatternFilterRequest filter, CancellationToken ct)
    {
        var query = db.ShiftPatterns.AsQueryable();
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var pattern = $"%{filter.Search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
            query = query.Where(p => EF.Functions.ILike(p.Code, pattern, "\\") || (p.Description != null && EF.Functions.ILike(p.Description, pattern, "\\")));
        }

        if (filter.Kind is { } kind) query = query.Where(p => p.Kind == kind);

        var ordered = query.OrderByDirection(p => p.Code, filter.IsDescending(defaultDescending: false));
        return Success(await ordered.Select(ShiftPatternResponse.Projection).ToPagedAsync(filter.Offset, filter.Limit, ct));
    }

    [HttpGet("{id:guid}"), HasPrivilege<ShiftPattern>(CrudAction.Read)]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct) =>
        Success(await db.ShiftPatterns.Where(p => p.Id == id).Select(ShiftPatternResponse.Projection).FirstOrDefaultAsync(ct) ?? throw ApiException.NotFound());

    [HttpPost, HasPrivilege<ShiftPattern>(CrudAction.Create)]
    public async Task<IActionResult> Create(CreateShiftPatternRequest request, CancellationToken ct)
    {
        var code = request.Code!.Trim().ToUpperInvariant();
        if (await db.ShiftPatterns.AnyAsync(p => p.Code == code, ct)) throw ApiException.Conflict("Dieser Code ist bereits vergeben.");

        var pattern = new ShiftPattern
        {
            Code = code,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            Kind = request.Kind!.Value,
            WeekCount = request.WeekCount!.Value
        };
        db.ShiftPatterns.Add(pattern);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw ApiException.Conflict("Dieser Code ist bereits vergeben.");
        }

        return CreatedData(ShiftPatternResponse.Projection.Compile()(pattern));
    }

    [HttpPatch("{id:guid}"), HasPrivilege<ShiftPattern>(CrudAction.Update)]
    public async Task<IActionResult> Update(Guid id, UpdateShiftPatternRequest request, CancellationToken ct)
    {
        var pattern = await db.ShiftPatterns.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw ApiException.NotFound();

        if (request.Description is not null) pattern.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        if (request.Kind is not null) pattern.Kind = request.Kind.Value;
        if (request.WeekCount is not null) pattern.WeekCount = request.WeekCount.Value;

        await db.SaveChangesAsync(ct);
        return Success(ShiftPatternResponse.Projection.Compile()(pattern));
    }

    [HttpDelete("{id:guid}"), HasPrivilege<ShiftPattern>(CrudAction.Delete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var pattern = await db.ShiftPatterns.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw ApiException.NotFound();
        db.ShiftPatterns.Remove(pattern); // noch keine Zuordnung zu Mitarbeitern, nichts verweist darauf
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
