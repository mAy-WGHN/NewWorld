using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Notifications.Contracts;
using Portal.Api.Http;
using Portal.Core;
using Portal.Core.Reports;
using Portal.Infrastructure;
using Portal.Infrastructure.Reports;
using Shared.Authorization;
using Shared.Infrastructure.Messaging;

namespace Portal.Api.Reports;

/// <summary>
/// Reports (incident reports): filing is public and rate limited, everything else needs a privilege and is limited to the stores the person reaches.
/// The notification about a new report goes to the people set for its category and contains no content of the report (class 2).
/// </summary>
public static class ReportEndpoints
{
    public const string NotificationTemplate = "portal.report-created.handler";

    private const string StoresNotFound = "Eine oder mehrere Filialen wurden nicht gefunden oder sind archiviert.";

    public static WebApplication MapReportEndpoints(this WebApplication app)
    {
        app.MapGet("/api/reports", (HttpRequest request, ClaimsPrincipal user, ReportAccess access, PortalDbContext db, CancellationToken ct) =>
                ListAsync(request, access.Visible(user, PortalPrivileges.ReportRead), null, db, ct))
            .RequireAuthorization(AppPolicies.Privilege(PortalPrivileges.ReportRead));

        // Like the list, but always archived.
        app.MapGet("/api/reports/archived", (HttpRequest request, ClaimsPrincipal user, ReportAccess access, PortalDbContext db, CancellationToken ct) =>
                ListAsync(request, access.Visible(user, PortalPrivileges.ReportRead), true, db, ct))
            .RequireAuthorization(AppPolicies.Privilege(PortalPrivileges.ReportRead));

        app.MapGet("/api/reports/{id:guid}", async (Guid id, ClaimsPrincipal user, ReportAccess access, PortalDbContext db, CancellationToken ct) =>
            {
                var report = await access.FindAsync(user, PortalPrivileges.ReportRead, id, ct);
                return Answer.Ok((await BuildAsync(db, [report], ct))[0]);
            })
            .RequireAuthorization(AppPolicies.Privilege(PortalPrivileges.ReportRead));

        app.MapPost("/api/reports", CreateAsync).AllowAnonymous().RequireRateLimiting(RateLimitingOptions.ReportCreationPolicy);
        app.MapPatch("/api/reports/{id:guid}", UpdateAsync).RequireAuthorization(AppPolicies.Privilege(PortalPrivileges.ReportUpdate));

        // Soft delete; repeating it is harmless.
        app.MapDelete("/api/reports/{id:guid}", async (Guid id, ClaimsPrincipal user, ReportAccess access, PortalDbContext db, TimeProvider time, CancellationToken ct) =>
            {
                var report = await access.FindAsync(user, PortalPrivileges.ReportDelete, id, ct);
                if (!report.Archived)
                {
                    report.Archived = true;
                    report.UpdatedAt = time.GetUtcNow().UtcDateTime;
                    await db.SaveChangesAsync(ct);
                }

                return Answer.NoContent();
            })
            .RequireAuthorization(AppPolicies.Privilege(PortalPrivileges.ReportDelete));

        // Its own endpoint: PATCH cannot tell "not set" from "explicitly null".
        app.MapDelete("/api/reports/{id:guid}/parent", async (Guid id, ClaimsPrincipal user, ReportAccess access, PortalDbContext db, TimeProvider time, CancellationToken ct) =>
            {
                var report = await access.FindAsync(user, PortalPrivileges.ReportUpdate, id, ct);
                if (report.ParentReportId is not null)
                {
                    report.ParentReportId = null;
                    report.UpdatedAt = time.GetUtcNow().UtcDateTime;
                    await db.SaveChangesAsync(ct);
                }

                return Answer.Ok((await BuildAsync(db, [report], ct))[0]);
            })
            .RequireAuthorization(AppPolicies.Privilege(PortalPrivileges.ReportUpdate));

        return app;
    }

    private static async Task<IResult> ListAsync(HttpRequest request, IQueryable<ReportRecord> visible, bool? forceArchived, PortalDbContext db, CancellationToken ct)
    {
        var filter = ReportFilter.Parse(request.Query);
        var query = visible;
        if (filter.Status is { } status) { query = query.Where(r => r.Status == status); }
        if (filter.Category is { } category) { query = query.Where(r => r.Category == category); }
        if (filter.Severity is { } severity) { query = query.Where(r => r.Severity == severity); }
        if (filter.Source is { } source) { query = query.Where(r => r.Source == source); }
        if ((forceArchived ?? filter.Archived) is { } archived) { query = query.Where(r => r.Archived == archived); }
        if (filter.PersonnelNumber is { } personnelNumber) { query = query.Where(r => r.PersonnelNumber == personnelNumber); }
        if (filter.LocationId is { } locationId) { query = query.Where(r => r.Locations.Any(l => l.LocationId == locationId)); }
        if (filter.Search is { } search)
        {
            // case-insensitive "contains"; % _ and  in the search text count literally
            var pattern = "%" + search.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
            query = query.Where(r => EF.Functions.ILike(r.Description, pattern));
        }

        if (filter.Day is { } day)
        {
            var start = DateTime.SpecifyKind(day, DateTimeKind.Utc);
            var end = start.AddDays(1);
            query = query.Where(r => r.CreatedAt >= start && r.CreatedAt < end);
        }
        else if (filter.MonthStart is { } monthStart)
        {
            var end = monthStart.AddMonths(1);
            query = query.Where(r => r.CreatedAt >= monthStart && r.CreatedAt < end);
        }

        var total = await query.CountAsync(ct);
        var page = await Sort(query, filter.SortBy, filter.Descending).Skip(filter.Offset).Take(filter.Limit).Include(r => r.Locations).AsSplitQuery().ToListAsync(ct);
        var items = await BuildAsync(db, page, ct);
        return Answer.Ok(new PagedResult<ReportDto>(items, new PageMeta(total, filter.Offset, filter.Limit)));
    }

    // Severity is sorted by rank, not alphabetically. "priority": rank, then creation time, then id (version 7 ids are time ordered: stable tie-break).
    private static IOrderedQueryable<ReportRecord> Sort(IQueryable<ReportRecord> q, string sortBy, bool desc) => sortBy switch
    {
        "priority" => Direction(q, SeverityRank, desc).ThenBy(r => r.CreatedAt, desc).ThenBy(r => r.Id, desc),
        "severity" => Direction(q, SeverityRank, desc).ThenBy(r => r.Id),
        "id" => Direction(q, r => r.Id, desc),
        "personnelNumber" => Direction(q, r => r.PersonnelNumber, desc).ThenBy(r => r.Id),
        "status" => Direction(q, r => r.Status, desc).ThenBy(r => r.Id),
        "source" => Direction(q, r => r.Source, desc).ThenBy(r => r.Id),
        "category" => Direction(q, r => r.Category, desc).ThenBy(r => r.Id),
        "description" => Direction(q, r => r.Description, desc).ThenBy(r => r.Id),
        "device" => Direction(q, r => r.Device, desc).ThenBy(r => r.Id),
        "noticedAt" => Direction(q, r => r.NoticedAt, desc).ThenBy(r => r.Id),
        "noticedHow" => Direction(q, r => r.NoticedHow, desc).ThenBy(r => r.Id),
        "priorActivity" => Direction(q, r => r.PriorActivity, desc).ThenBy(r => r.Id),
        "comment" => Direction(q, r => r.Comment, desc).ThenBy(r => r.Id),
        "archived" => Direction(q, r => r.Archived, desc).ThenBy(r => r.Id),
        "updatedAt" => Direction(q, r => r.UpdatedAt, desc).ThenBy(r => r.Id),
        _ => Direction(q, r => r.CreatedAt, desc).ThenBy(r => r.Id),
    };

    private static readonly System.Linq.Expressions.Expression<Func<ReportRecord, int>> SeverityRank = r =>
        r.Severity == ReportSeverity.Low ? 1 :
        r.Severity == ReportSeverity.Medium ? 2 :
        r.Severity == ReportSeverity.High ? 3 : 4;

    private static IOrderedQueryable<ReportRecord> Direction<TKey>(IQueryable<ReportRecord> q, System.Linq.Expressions.Expression<Func<ReportRecord, TKey>> key, bool desc) =>
        desc ? q.OrderByDescending(key) : q.OrderBy(key);

    private static IOrderedQueryable<ReportRecord> ThenBy<TKey>(this IOrderedQueryable<ReportRecord> q, System.Linq.Expressions.Expression<Func<ReportRecord, TKey>> key, bool desc) =>
        desc ? q.ThenByDescending(key) : q.ThenBy(key);

    // ------------------------------------------------------------------ create

    private static async Task<IResult> CreateAsync(
        CreateReportRequest request, PortalDbContext db, Outbox<PortalDbContext> outbox, TimeProvider time, CancellationToken ct)
    {
        var validation = new Validator();
        if (request.PersonnelNumber is null)
        {
            validation.Add("personnelNumber", Validator.Required);
        }
        else if (request.PersonnelNumber < 1)
        {
            validation.Add("personnelNumber", "Dieser Wert muss positiv sein.");
        }

        if (request.Category is null) { validation.Add("category", Validator.Required); }
        if (request.Severity is null) { validation.Add("severity", Validator.Required); }
        validation.RequiredText("description", request.Description);
        validation.MaxLength("device", request.Device, 120);
        if (request.LocationIds is null)
        {
            validation.Add("locationIds", Validator.Required);
        }
        else if (request.LocationIds.Count == 0)
        {
            validation.Add("locationIds", "Es muss mindestens eine Filiale angegeben werden.");
        }

        validation.ThrowIfInvalid();

        var storeIds = await ResolveStoresAsync(db, request.LocationIds!, ct);
        var now = time.GetUtcNow();
        var report = new ReportRecord
        {
            PersonnelNumber = request.PersonnelNumber!.Value,
            Source = ReportSource.Employee, // public reports are always from an employee
            Category = request.Category!.Value,
            Severity = request.Severity!.Value,
            Description = request.Description!,
            Device = request.Device,
            NoticedAt = request.NoticedAt,
            NoticedHow = request.NoticedHow,
            PriorActivity = request.PriorActivity,
            CreatedAt = now.UtcDateTime,
            UpdatedAt = now.UtcDateTime,
            Locations = [.. storeIds.Select(id => new ReportLocationRecord { LocationId = id })],
        };
        db.Reports.Add(report);

        // The notification is written in the same transaction (outbox): either the report and the order exist, or neither.
        var recipients = await db.ReportRecipients.Where(r => r.Category == report.Category).Select(r => r.EmployeeId).ToListAsync(ct);
        if (recipients.Count > 0)
        {
            outbox.Send(new SendNotification(
                Guid.CreateVersion7(now), report.Id, now, report.Id, recipients, NotificationTemplate,
                new Dictionary<string, string>(), [NotificationChannel.InApp, NotificationChannel.Push], $"/reports/{report.Id}"));
        }

        await db.SaveChangesAsync(ct);
        return Answer.Created((await BuildAsync(db, [report], ct))[0]);
    }

    // ------------------------------------------------------------------ update

    private static async Task<IResult> UpdateAsync(
        Guid id, UpdateReportRequest request, ClaimsPrincipal user, ReportAccess access, PortalDbContext db, TimeProvider time, CancellationToken ct)
    {
        var validation = new Validator();
        if (request.PersonnelNumber < 1)
        {
            validation.Add("personnelNumber", "Dieser Wert muss positiv sein.");
        }

        validation.MaxLength("device", request.Device, 120);
        validation.ThrowIfInvalid();

        var report = await access.FindAsync(user, PortalPrivileges.ReportUpdate, id, ct);
        if (request.ParentReportId is { } parentId)
        {
            await SetParentAsync(db, report, parentId, ct);
        }

        if (request.LocationIds is not null)
        {
            var storeIds = await ResolveStoresAsync(db, request.LocationIds, ct);
            var reach = access.ReachOf(user, PortalPrivileges.ReportUpdate);
            if (reach is { Unrestricted: false })
            {
                // Somebody with a limited reach only changes their part: new stores only inside the reach, stores outside it stay as they are.
                if (storeIds.Any(s => !reach.StoreIds.Contains(s)))
                {
                    throw new ApiException(StatusCodes.Status403Forbidden, EnvelopeAuthorizationResultHandler.Message("update"));
                }

                storeIds.AddRange(report.Locations.Select(l => l.LocationId).Where(s => !reach.StoreIds.Contains(s) && !storeIds.Contains(s)));
            }

            foreach (var gone in report.Locations.Where(l => !storeIds.Contains(l.LocationId)).ToList())
            {
                report.Locations.Remove(gone);
            }

            foreach (var storeId in storeIds.Where(s => report.Locations.All(l => l.LocationId != s)))
            {
                report.Locations.Add(new ReportLocationRecord { ReportId = report.Id, LocationId = storeId });
            }
        }

        // The source cannot be changed after filing.
        if (request.PersonnelNumber is { } personnelNumber) { report.PersonnelNumber = personnelNumber; }
        if (request.Status is { } status) { report.Status = status; }
        if (request.Category is { } category) { report.Category = category; }
        if (request.Description is not null) { report.Description = request.Description; }
        if (request.Severity is { } severity) { report.Severity = severity; }
        if (request.Device is not null) { report.Device = request.Device; }
        if (request.NoticedAt is not null) { report.NoticedAt = request.NoticedAt; }
        if (request.NoticedHow is not null) { report.NoticedHow = request.NoticedHow; }
        if (request.PriorActivity is not null) { report.PriorActivity = request.PriorActivity; }
        if (request.Comment is not null) { report.Comment = request.Comment; }
        if (request.Archived is { } archived) { report.Archived = archived; }

        report.UpdatedAt = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return Answer.Ok((await BuildAsync(db, [report], ct))[0]);
    }

    // The only place that sets a parent: no second code path without the cycle check.
    private static async Task SetParentAsync(PortalDbContext db, ReportRecord report, Guid parentId, CancellationToken ct)
    {
        if (parentId == report.Id)
        {
            throw ApiException.BadRequest("Ein Report kann nicht sein eigener Hauptreport sein.");
        }

        if (!await db.Reports.AnyAsync(r => r.Id == parentId, ct))
        {
            throw ApiException.NotFound("Hauptreport wurde nicht gefunden.");
        }

        // Walk up the parent's ancestors; if the report itself turns up, it would be a cycle.
        var seen = new HashSet<Guid>();
        Guid? cursor = parentId;
        while (cursor is { } current)
        {
            if (current == report.Id)
            {
                throw ApiException.BadRequest("Zyklische Verknüpfung nicht erlaubt.");
            }

            if (!seen.Add(current))
            {
                break;
            }

            cursor = await db.Reports.Where(r => r.Id == current).Select(r => r.ParentReportId).FirstOrDefaultAsync(ct);
        }

        report.ParentReportId = parentId;
    }

    // Duplicates are merged; unknown or closed stores are a 404 (the stores are the copy of BC).
    private static async Task<List<Guid>> ResolveStoresAsync(PortalDbContext db, IEnumerable<Guid> ids, CancellationToken ct)
    {
        var distinct = ids.Distinct().ToList();
        var usable = await db.Stores.AsNoTracking().Where(s => distinct.Contains(s.StoreId) && s.IsActive).Select(s => s.StoreId).ToListAsync(ct);
        return usable.Count == distinct.Count ? distinct : throw ApiException.NotFound(StoresNotFound);
    }

    // ------------------------------------------------------------------ answer

    /// <summary>The answers of the given reports with the stores (names from the copy) and the ids of the child reports.</summary>
    private static async Task<List<ReportDto>> BuildAsync(PortalDbContext db, IReadOnlyList<ReportRecord> reports, CancellationToken ct)
    {
        var ids = reports.Select(r => r.Id).ToList();
        var storeIds = reports.SelectMany(r => r.Locations.Select(l => l.LocationId)).Distinct().ToList();
        var stores = await db.Stores.AsNoTracking().Where(s => storeIds.Contains(s.StoreId)).ToDictionaryAsync(s => s.StoreId, ct);
        var children = (await db.Reports.AsNoTracking().Where(c => c.ParentReportId != null && ids.Contains(c.ParentReportId.Value))
                .Select(c => new { c.Id, Parent = c.ParentReportId!.Value }).ToListAsync(ct))
            .ToLookup(c => c.Parent, c => c.Id);

        return
        [
            .. reports.Select(r => new ReportDto(
                r.Id, r.PersonnelNumber, r.Status, r.Source, r.Category, r.Description, r.Severity,
                [.. r.Locations.Select(l => stores.TryGetValue(l.LocationId, out var s)
                        ? new LocationDto(s.StoreId, s.Name, s.StoreNumber, !s.IsActive)
                        : new LocationDto(l.LocationId, string.Empty, string.Empty, false))
                    .OrderBy(l => l.StoreCode, StringComparer.Ordinal).ThenBy(l => l.Id)],
                r.Device, r.NoticedAt, r.NoticedHow, r.PriorActivity, r.Comment, r.Archived, r.ParentReportId,
                [.. children[r.Id]], r.CreatedAt, r.UpdatedAt)),
        ];
    }
}
