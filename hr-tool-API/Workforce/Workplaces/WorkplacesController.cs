using HrTool_API.Authentication;
using HrTool_API.Common.Api;
using HrTool_API.Contracts;
using HrTool_API.Data;
using HrTool_API.Domain.Entities;
using HrTool_API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrTool_API.Workforce;

/// <summary>Die Arbeitsorte (Stamm aus BC). Nur lesen und die Filiale zuordnen; angelegt werden sie aus dem Stamm (<see cref="ReferenceDataMaintenance"/>).</summary>
[Route("api/workplaces")]
public class WorkplacesController(HrToolDbContext db, ILocationDirectory locationDirectory) : ApiControllerBase
{
    [HttpGet, HasPrivilege<Workplace>(CrudAction.Read)]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Success(await db.Workplaces.OrderBy(w => w.Code).Select(WorkplaceResponse.Projection).ToListAsync(ct));

    [HttpGet("{id:guid}"), HasPrivilege<Workplace>(CrudAction.Read)]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct) =>
        Success(await db.Workplaces.Where(w => w.Id == id).Select(WorkplaceResponse.Projection).FirstOrDefaultAsync(ct) ?? throw ApiException.NotFound());

    [HttpPatch("{id:guid}"), HasPrivilege<Workplace>(CrudAction.Update)]
    public async Task<IActionResult> Update(Guid id, UpdateWorkplaceRequest request, CancellationToken ct)
    {
        var workplace = await db.Workplaces.FirstOrDefaultAsync(w => w.Id == id, ct) ?? throw ApiException.NotFound();

        if (request.LocationId is { } locationId && locationId != workplace.LocationId)
        {
            // Eine unbekannte Filiale kann gerade erst angelegt worden sein: vor dem 404 einmal frisch nachsehen.
            var known = await locationDirectory.GetAllAsync(ct);
            if (!known.ContainsKey(locationId)) known = await locationDirectory.GetAllAsync(ct, forceRefresh: true);
            if (!known.ContainsKey(locationId)) throw ApiException.NotFound("Die Filiale wurde nicht gefunden.");
            if (await db.Workplaces.AnyAsync(w => w.LocationId == locationId && w.Id != id, ct))
                throw ApiException.Conflict("Diese Filiale gehört bereits zu einem anderen Arbeitsort.");

            workplace.LocationId = locationId;
        }

        await db.SaveChangesAsync(ct);
        return Success(WorkplaceResponse.Projection.Compile()(workplace));
    }
}
