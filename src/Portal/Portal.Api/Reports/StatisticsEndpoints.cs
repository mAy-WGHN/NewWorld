using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Portal.Api.Http;
using Portal.Core;
using Portal.Core.Reports;
using Portal.Infrastructure;
using Portal.Infrastructure.Reports;
using Shared.Authorization;

namespace Portal.Api.Reports;

public sealed record MonthlyStatistic(string Month, int Employee, int System, int Total);

public sealed record MonthlyCategoryStatistic(string Month, int Bug, int Virus, int Theft, int Other, int Suggestion, int Total);

public sealed record LocationSeverityStatistic(Guid LocationId, string LocationName, int Low, int Medium, int High, int Critical, int Total);

public sealed record PersonnelNumberStatistic(int PersonnelNumber, int Low, int Medium, int High, int Critical, int Total);

/// <summary>
/// Statistics of reports by creation time (UTC; archived reports count). Combinations that do not occur are 0, months and stores without reports are
/// missing. A report with several stores counts in each of them. Only the reports the privilege reaches are counted.
/// </summary>
public static class StatisticsEndpoints
{
    public static WebApplication MapStatisticsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/statistics/monthly", async (HttpRequest request, ClaimsPrincipal user, ReportAccess access, CancellationToken ct) =>
            {
                var rows = await InRange(request, access, user)
                    .GroupBy(r => new { r.CreatedAt.Year, r.CreatedAt.Month, r.Source })
                    .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Source, Count = g.Count() })
                    .ToListAsync(ct);
                return Answer.Ok(rows.GroupBy(r => (r.Year, r.Month)).OrderBy(g => g.Key).Select(g =>
                {
                    var employee = g.Where(x => x.Source == ReportSource.Employee).Sum(x => x.Count);
                    var system = g.Where(x => x.Source == ReportSource.System).Sum(x => x.Count);
                    return new MonthlyStatistic(MonthKey(g.Key.Year, g.Key.Month), employee, system, employee + system);
                }).ToList());
            })
            .RequireAuthorization(AppPolicies.Privilege(PortalPrivileges.StatisticRead));

        app.MapGet("/api/statistics/monthly-by-category", async (HttpRequest request, ClaimsPrincipal user, ReportAccess access, CancellationToken ct) =>
            {
                var rows = await InRange(request, access, user)
                    .GroupBy(r => new { r.CreatedAt.Year, r.CreatedAt.Month, r.Category })
                    .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Category, Count = g.Count() })
                    .ToListAsync(ct);
                return Answer.Ok(rows.GroupBy(r => (r.Year, r.Month)).OrderBy(g => g.Key).Select(g =>
                {
                    int Of(ReportCategory c) => g.Where(x => x.Category == c).Sum(x => x.Count);
                    return new MonthlyCategoryStatistic(
                        MonthKey(g.Key.Year, g.Key.Month),
                        Of(ReportCategory.Bug), Of(ReportCategory.Virus), Of(ReportCategory.Theft), Of(ReportCategory.Other), Of(ReportCategory.Suggestion), g.Sum(x => x.Count));
                }).ToList());
            })
            .RequireAuthorization(AppPolicies.Privilege(PortalPrivileges.StatisticRead));

        app.MapGet("/api/statistics/by-location-severity", async (HttpRequest request, ClaimsPrincipal user, ReportAccess access, PortalDbContext db, CancellationToken ct) =>
            {
                var rows = await InRange(request, access, user)
                    .SelectMany(r => r.Locations, (r, l) => new { l.LocationId, r.Severity })
                    .GroupBy(x => new { x.LocationId, x.Severity })
                    .Select(g => new { g.Key.LocationId, g.Key.Severity, Count = g.Count() })
                    .ToListAsync(ct);
                var ids = rows.Select(r => r.LocationId).Distinct().ToList();
                var names = await db.Stores.AsNoTracking().Where(s => ids.Contains(s.StoreId)).ToDictionaryAsync(s => s.StoreId, s => s.Name, ct);
                string NameOf(Guid id) => names.GetValueOrDefault(id, string.Empty);
                return Answer.Ok(rows.GroupBy(r => (r.LocationId, Name: NameOf(r.LocationId))).OrderBy(g => g.Key.Name, StringComparer.Ordinal).ThenBy(g => g.Key.LocationId).Select(g =>
                {
                    int Of(ReportSeverity s) => g.Where(x => x.Severity == s).Sum(x => x.Count);
                    return new LocationSeverityStatistic(
                        g.Key.LocationId, g.Key.Name, Of(ReportSeverity.Low), Of(ReportSeverity.Medium), Of(ReportSeverity.High), Of(ReportSeverity.Critical), g.Sum(x => x.Count));
                }).ToList());
            })
            .RequireAuthorization(AppPolicies.Privilege(PortalPrivileges.StatisticRead));

        app.MapGet("/api/statistics/by-personnel-number", async (HttpRequest request, ClaimsPrincipal user, ReportAccess access, CancellationToken ct) =>
            {
                var rows = await InRange(request, access, user)
                    .GroupBy(r => new { r.PersonnelNumber, r.Severity })
                    .Select(g => new { g.Key.PersonnelNumber, g.Key.Severity, Count = g.Count() })
                    .ToListAsync(ct);
                return Answer.Ok(rows.GroupBy(r => r.PersonnelNumber).OrderBy(g => g.Key).Select(g =>
                {
                    int Of(ReportSeverity s) => g.Where(x => x.Severity == s).Sum(x => x.Count);
                    return new PersonnelNumberStatistic(
                        g.Key, Of(ReportSeverity.Low), Of(ReportSeverity.Medium), Of(ReportSeverity.High), Of(ReportSeverity.Critical), g.Sum(x => x.Count));
                }).ToList());
            })
            .RequireAuthorization(AppPolicies.Privilege(PortalPrivileges.StatisticRead));

        return app;
    }

    /// <summary>The reports the privilege reaches within <c>from</c> (first day of that month) and <c>to</c> (to the end of that month, inclusive).</summary>
    private static IQueryable<ReportRecord> InRange(HttpRequest request, ReportAccess access, ClaimsPrincipal user)
    {
        var errors = new Validator();
        var from = ReportFilter.Month(request.Query, "from", errors);
        var to = ReportFilter.Month(request.Query, "to", errors);
        errors.ThrowIfInvalid();

        var query = access.Visible(user, PortalPrivileges.StatisticRead);
        if (from is { } start)
        {
            query = query.Where(r => r.CreatedAt >= start);
        }

        if (to is { } last)
        {
            var before = last.AddMonths(1);
            query = query.Where(r => r.CreatedAt < before);
        }

        return query;
    }

    private static string MonthKey(int year, int month) => string.Create(CultureInfo.InvariantCulture, $"{year:D4}-{month:D2}");
}
