using System.Linq.Expressions;
using Portal.Common.Api;
using Portal.Contracts;
using Portal.Data;
using Portal.Domain.Entities;
using Portal.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Portal.Services;

public class ReportQueryService(IrsDbContext db, ILocationDirectory locationDirectory)
{
    /// <summary>Liste mit Filtern, Sortierung und Paginierung. forceArchived überschreibt den Archiv-Filter.</summary>
    public async Task<PagedResult<ReportResponse>> ListAsync(ReportFilterRequest f, bool? forceArchived, CancellationToken ct)
    {
        var query = db.Reports.AsQueryable();

        if (f.Status is { } status) query = query.Where(r => r.Status == status);
        if (f.Category is { } category) query = query.Where(r => r.Category == category);
        if (f.Severity is { } severity) query = query.Where(r => r.Severity == severity);
        if (f.Source is { } source) query = query.Where(r => r.Source == source);
        if ((forceArchived ?? f.Archived) is { } archived) query = query.Where(r => r.Archived == archived);
        if (f.PersonnelNumber is { } personnelNumber) query = query.Where(r => r.PersonnelNumber == personnelNumber);
        if (f.LocationId is { } locationId) query = query.Where(r => r.ReportLocations.Any(l => l.LocationId == locationId));

        var search = f.Search?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(search)) query = query.Where(r => r.Description.ToLower().Contains(search));

        if (f.Day is { } day)
        {
            var start = DateTime.SpecifyKind(day.Date, DateTimeKind.Utc);
            var end = start.AddDays(1);
            query = query.Where(r => r.CreatedAt >= start && r.CreatedAt < end);
        }
        else if (f.Month is { } month)
        {
            var start = DateTime.SpecifyKind(
                new DateTime(int.Parse(month[..4]), int.Parse(month[5..]), 1), DateTimeKind.Utc);
            var end = start.AddMonths(1);
            query = query.Where(r => r.CreatedAt >= start && r.CreatedAt < end);
        }

        var ordered = ApplySort(query, f.SortBy ?? "priority", f.SortOrder != "asc");
        var page = await ordered.Select(ReportRow.Projection).ToPagedAsync(f.Offset, f.Limit, ct);
        var locations = await locationDirectory.GetAllAsync(ct);
        return new PagedResult<ReportResponse>(page.Items.Select(r => ReportResponse.From(r, locations)).ToList(), page.Meta);
    }

    // Severity wird nach Rang sortiert (nicht alphabetisch), direkt in SQL per CASE.
    // priority: Rang, dann createdAt, dann ID (UUIDv7 = zeitlich sortiert, stabiler Tiebreaker).
    private static IOrderedQueryable<Report> ApplySort(IQueryable<Report> q, string sortBy, bool desc) => sortBy switch
    {
        "priority" => q.OrderByDirection(SeverityRank, desc)
            .ThenByDirection(r => r.CreatedAt, desc).ThenByDirection(r => r.Id, desc),
        "severity" => q.OrderByDirection(SeverityRank, desc).ThenBy(r => r.Id),
        "id" => q.OrderByDirection(r => r.Id, desc),
        "personnelNumber" => q.OrderByDirection(r => r.PersonnelNumber, desc).ThenBy(r => r.Id),
        "status" => q.OrderByDirection(r => r.Status, desc).ThenBy(r => r.Id),
        "source" => q.OrderByDirection(r => r.Source, desc).ThenBy(r => r.Id),
        "category" => q.OrderByDirection(r => r.Category, desc).ThenBy(r => r.Id),
        "description" => q.OrderByDirection(r => r.Description, desc).ThenBy(r => r.Id),
        "device" => q.OrderByDirection(r => r.Device, desc).ThenBy(r => r.Id),
        "noticedAt" => q.OrderByDirection(r => r.NoticedAt, desc).ThenBy(r => r.Id),
        "noticedHow" => q.OrderByDirection(r => r.NoticedHow, desc).ThenBy(r => r.Id),
        "priorActivity" => q.OrderByDirection(r => r.PriorActivity, desc).ThenBy(r => r.Id),
        "comment" => q.OrderByDirection(r => r.Comment, desc).ThenBy(r => r.Id),
        "archived" => q.OrderByDirection(r => r.Archived, desc).ThenBy(r => r.Id),
        "updatedAt" => q.OrderByDirection(r => r.UpdatedAt, desc).ThenBy(r => r.Id),
        _ => q.OrderByDirection(r => r.CreatedAt, desc).ThenBy(r => r.Id)
    };

    private static readonly Expression<Func<Report, int>> SeverityRank = r =>
        r.Severity == ReportSeverity.Low ? 1 :
        r.Severity == ReportSeverity.Medium ? 2 :
        r.Severity == ReportSeverity.High ? 3 : 4;

    public async Task<ReportResponse?> GetAsync(Guid id, CancellationToken ct)
    {
        var row = await db.Reports.Where(r => r.Id == id).Select(ReportRow.Projection).FirstOrDefaultAsync(ct);
        return row is null ? null : ReportResponse.From(row, await locationDirectory.GetAllAsync(ct));
    }
}
