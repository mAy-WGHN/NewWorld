using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Portal.Api.Http;
using Portal.Infrastructure;
using Portal.Infrastructure.Reports;
using Shared.Authorization;

namespace Portal.Api.Reports;

/// <summary>
/// The reach of a person for one privilege (from the local permission copy) and what follows for reports: a report is seen by everybody who reaches
/// at least one of its stores ("one store is enough"). Out of reach but existing = 403 with the fixed text, not existing = 404.
/// </summary>
public sealed class ReportAccess(IPermissionCache cache, PortalDbContext db)
{
    public PrivilegeReach? ReachOf(ClaimsPrincipal user, string privilege) =>
        Guid.TryParse(user.FindFirst(AppClaimTypes.EmployeeId)?.Value, out var id)
        && cache.TryGet(id, out var snapshot)
        && snapshot is not null
        && snapshot.Privileges.TryGetValue(privilege, out var reach)
            ? reach
            : null;

    /// <summary>The reports the privilege reaches. Without any reach nothing (fail closed).</summary>
    public IQueryable<ReportRecord> Visible(ClaimsPrincipal user, string privilege)
    {
        var reach = ReachOf(user, privilege);
        if (reach is null)
        {
            return db.Reports.Where(_ => false);
        }

        if (reach.Unrestricted)
        {
            return db.Reports;
        }

        var stores = reach.StoreIds.ToArray();
        return db.Reports.Where(r => r.Locations.Any(l => stores.Contains(l.LocationId)));
    }

    /// <summary>The report with its stores, if the privilege reaches it; otherwise 403 (exists) or 404.</summary>
    public async Task<ReportRecord> FindAsync(ClaimsPrincipal user, string privilege, Guid id, CancellationToken ct)
    {
        var report = await Visible(user, privilege).Include(r => r.Locations).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (report is not null)
        {
            return report;
        }

        throw await db.Reports.AnyAsync(r => r.Id == id, ct)
            ? new ApiException(StatusCodes.Status403Forbidden, EnvelopeAuthorizationResultHandler.Message(privilege.Split(':').Last()))
            : ApiException.NotFound();
    }
}
