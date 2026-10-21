using Rollen_Rechte.Authentication;
using Rollen_Rechte.Common.Api;
using Rollen_Rechte.Contracts;
using Rollen_Rechte.Data;

namespace Rollen_Rechte.Services;

/// <summary>Gemeinsame Listenlogik für aktive und archivierte Filialen.</summary>
public static class LocationListing
{
    public static Task<PagedResult<LocationResponse>> ListAsync(
        AccessDbContext db, LocationFilterRequest filter, bool archived, ScopeSet scope, CancellationToken ct)
    {
        var query = db.Locations.InScope(scope).Where(l => l.Archived == archived);

        var search = filter.Search?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(search))
        {
            // Padding-agnostisch: "10" findet "0010" und "0010" findet "10".
            var stripped = search.TrimStart('0');
            if (stripped.Length == 0) stripped = search;

            query = query.Where(l =>
                l.Name.ToLower().Contains(search) ||
                l.StoreCode.ToLower().Contains(search) ||
                l.StoreCode.TrimStart('0').Contains(stripped));
        }

        var desc = filter.IsDescending(false);
        var ordered = (filter.SortBy ?? "storeCode") switch
        {
            "id" => query.OrderByDirection(l => l.Id, desc),
            "name" => query.OrderByDirection(l => l.Name, desc),
            "archived" => query.OrderByDirection(l => l.Archived, desc),
            _ => query.OrderByDirection(l => l.StoreCode, desc)
        };

        return ordered.ThenBy(l => l.Id).Select(LocationResponse.Projection)
            .ToPagedAsync(filter.Offset, filter.Limit, ct);
    }
}
