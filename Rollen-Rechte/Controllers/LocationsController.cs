using Rollen_Rechte.Domain.Entities;
using Rollen_Rechte.Authentication;
using Rollen_Rechte.Common.Api;
using Rollen_Rechte.Common.RateLimiting;
using Rollen_Rechte.Contracts;
using Rollen_Rechte.Data;
using Rollen_Rechte.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Rollen_Rechte.Controllers;

[Route("api/locations")]
public class LocationsController(AccessDbContext db, ScopeContext scope) : ApiControllerBase
{
    private const string StoreCodeTaken = "Store-Code wird bereits verwendet.";

    [HttpGet, AllowAnonymous]
    public async Task<IActionResult> List([FromQuery] LocationFilterRequest filter, CancellationToken ct) =>
        Success(await LocationListing.ListAsync(db, filter, archived: false, ScopeSet.Everything, ct));

    [HttpGet("{id:guid}"), AllowAnonymous]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct) =>
        Success(await db.Locations.Where(l => l.Id == id).Select(LocationResponse.Projection)
            .FirstOrDefaultAsync(ct) ?? throw ApiException.NotFound());

    [HttpPost, HasPrivilege<Location>(CrudAction.Create), EnableRateLimiting(RateLimitPolicies.LocationCreation)]
    public async Task<IActionResult> Create(CreateLocationRequest request, CancellationToken ct)
    {
        // Eine neue Filiale liegt in keinem bestehenden Scope: Anlegen verlangt uneingeschränkte Reichweite.
        if (!scope.Current.All) throw AuthMessages.Forbidden(CrudAction.Create);

        var storeCode = request.StoreCode!.Trim();
        if (await db.Locations.AnyAsync(l => l.StoreCode == storeCode, ct))
            throw ApiException.Conflict(StoreCodeTaken);

        var location = new Domain.Entities.Location { Name = request.Name!.Trim(), StoreCode = storeCode };
        db.Locations.Add(location);
        await SaveUniqueAsync(ct);
        return CreatedData(LocationResponse.From(location));
    }

    [HttpPatch("{id:guid}"), HasPrivilege<Location>(CrudAction.Update)]
    public async Task<IActionResult> Update(Guid id, UpdateLocationRequest request, CancellationToken ct)
    {
        var location = await FindInScopeAsync(id, CrudAction.Update, ct);

        if (request.StoreCode is not null)
        {
            var storeCode = request.StoreCode.Trim();
            if (await db.Locations.AnyAsync(l => l.StoreCode == storeCode && l.Id != id, ct))
                throw ApiException.Conflict(StoreCodeTaken);
            location.StoreCode = storeCode;
        }

        if (request.Name is not null) location.Name = request.Name.Trim();
        if (request.Archived is not null) location.Archived = request.Archived.Value;

        await SaveUniqueAsync(ct);
        return Success(LocationResponse.From(location));
    }

    [HttpDelete("{id:guid}"), HasPrivilege<Location>(CrudAction.Delete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var location = await FindInScopeAsync(id, CrudAction.Delete, ct);
        location.Archived = true;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // Außerhalb des eigenen Scopes: 403 mit der vordefinierten Meldung; nicht vorhanden: 404.
    private async Task<Domain.Entities.Location> FindInScopeAsync(Guid id, CrudAction action, CancellationToken ct)
    {
        var location = await db.Locations.FindAsync([id], ct) ?? throw ApiException.NotFound();
        if (!scope.Current.AllowsLocation(location.Id)) throw AuthMessages.Forbidden(action);
        return location;
    }

    // Fängt die Race-Condition zwischen Prüfung und Unique-Index ab.
    private async Task SaveUniqueAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            throw ApiException.Conflict(StoreCodeTaken);
        }
    }
}
