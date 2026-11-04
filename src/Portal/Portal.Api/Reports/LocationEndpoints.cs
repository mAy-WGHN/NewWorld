using Microsoft.EntityFrameworkCore;
using Portal.Api.Http;
using Portal.Infrastructure;

namespace Portal.Api.Reports;

/// <summary>
/// The stores to choose from in the report form (<c>GET /api/locations</c>, like the existing frontend reads it). Public, because the report form
/// is public. Organisation data only (class 1: name and store number). Only open stores: a report for a closed store is refused. The list itself
/// is a copy from BC (<c>StoreChanged</c>) and cannot be changed here.
/// </summary>
public static class LocationEndpoints
{
    public static WebApplication MapLocationEndpoints(this WebApplication app)
    {
        app.MapGet("/api/locations", async (PortalDbContext db, CancellationToken ct) =>
            {
                var items = await db.Stores.AsNoTracking()
                    .Where(s => s.IsActive)
                    .OrderBy(s => s.StoreNumber)
                    .Select(s => new LocationDto(s.StoreId, s.Name, s.StoreNumber, false))
                    .ToListAsync(ct);
                return Answer.Ok(new PagedResult<LocationDto>(items, new PageMeta(items.Count, 0, null)));
            })
            .AllowAnonymous();

        return app;
    }
}
