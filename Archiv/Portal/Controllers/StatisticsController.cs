using Portal.Authentication;
using Portal.Common.Api;
using Portal.Contracts;
using Portal.Data;
using Portal.Domain.Entities;
using Portal.Domain.Enums;
using Portal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Portal.Controllers;

/// <summary>
/// Alle Endpunkte filtern nach reports.created_at (ohne Zeitzone, unverändert). Archivierte Reports
/// zählen mit. Nicht vorhandene Kombinationen sind 0, Monate/Filialen ohne Reports fehlen.
/// </summary>
[Route("api/statistics")]
public class StatisticsController(IrsDbContext db, ILocationDirectory locationDirectory) : ApiControllerBase
{
    [HttpGet("monthly"), HasPrivilege(Privileges.StatisticRead)]
    public async Task<IActionResult> Monthly([FromQuery] StatisticsFilterRequest filter, CancellationToken ct)
    {
        var rows = await InRange(filter)
            .GroupBy(r => new { r.CreatedAt.Year, r.CreatedAt.Month, r.Source })
            .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Source, Count = g.Count() })
            .ToListAsync(ct);

        var result = rows.GroupBy(r => (r.Year, r.Month)).OrderBy(g => g.Key).Select(g =>
        {
            var employee = Count(g, x => x.Source == ReportSource.Employee, x => x.Count);
            var system = Count(g, x => x.Source == ReportSource.System, x => x.Count);
            return new MonthlyStatistic(MonthKey(g.Key.Year, g.Key.Month), employee, system, employee + system);
        }).ToList();

        return Success(result);
    }

    [HttpGet("monthly-by-category"), HasPrivilege(Privileges.StatisticRead)]
    public async Task<IActionResult> MonthlyByCategory([FromQuery] StatisticsFilterRequest filter, CancellationToken ct)
    {
        var rows = await InRange(filter)
            .GroupBy(r => new { r.CreatedAt.Year, r.CreatedAt.Month, r.Category })
            .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Category, Count = g.Count() })
            .ToListAsync(ct);

        var result = rows.GroupBy(r => (r.Year, r.Month)).OrderBy(g => g.Key).Select(g =>
        {
            int Of(ReportCategory c) => Count(g, x => x.Category == c, x => x.Count);
            return new MonthlyCategoryStatistic(
                MonthKey(g.Key.Year, g.Key.Month),
                Of(ReportCategory.Bug), Of(ReportCategory.Virus), Of(ReportCategory.Theft),
                Of(ReportCategory.Other), Of(ReportCategory.Suggestion), g.Sum(x => x.Count));
        }).ToList();

        return Success(result);
    }

    // Ein Report mit n Filialen zählt in jeder dieser Filialen mit (Join über report_location).
    [HttpGet("by-location-severity"), HasPrivilege(Privileges.StatisticRead)]
    public async Task<IActionResult> ByLocationAndSeverity([FromQuery] StatisticsFilterRequest filter, CancellationToken ct)
    {
        var rows = await InRange(filter)
            .SelectMany(r => r.ReportLocations, (r, l) => new { l.LocationId, r.Severity })
            .GroupBy(x => new { x.LocationId, x.Severity })
            .Select(g => new { g.Key.LocationId, g.Key.Severity, Count = g.Count() })
            .ToListAsync(ct);

        // Namen kommen von Rollen-Rechte (dort liegen die Filialen).
        var names = await locationDirectory.GetAllAsync(ct);
        string NameOf(Guid id) => names.TryGetValue(id, out var l) ? l.Name : string.Empty;

        var result = rows.GroupBy(r => (r.LocationId, Name: NameOf(r.LocationId))).OrderBy(g => g.Key.Name, StringComparer.Ordinal).ThenBy(g => g.Key.LocationId).Select(g =>
        {
            int Of(ReportSeverity s) => Count(g, x => x.Severity == s, x => x.Count);
            return new LocationSeverityStatistic(
                g.Key.LocationId, g.Key.Name,
                Of(ReportSeverity.Low), Of(ReportSeverity.Medium), Of(ReportSeverity.High), Of(ReportSeverity.Critical),
                g.Sum(x => x.Count));
        }).ToList();

        return Success(result);
    }

    [HttpGet("by-personnel-number"), HasPrivilege(Privileges.StatisticRead)]
    public async Task<IActionResult> ByPersonnelNumber([FromQuery] StatisticsFilterRequest filter, CancellationToken ct)
    {
        var rows = await InRange(filter)
            .GroupBy(r => new { r.PersonnelNumber, r.Severity })
            .Select(g => new { g.Key.PersonnelNumber, g.Key.Severity, Count = g.Count() })
            .ToListAsync(ct);

        var result = rows.GroupBy(r => r.PersonnelNumber).OrderBy(g => g.Key).Select(g =>
        {
            int Of(ReportSeverity s) => Count(g, x => x.Severity == s, x => x.Count);
            return new PersonnelNumberStatistic(
                g.Key,
                Of(ReportSeverity.Low), Of(ReportSeverity.Medium), Of(ReportSeverity.High), Of(ReportSeverity.Critical),
                g.Sum(x => x.Count));
        }).ToList();

        return Success(result);
    }

    private IQueryable<Report> InRange(StatisticsFilterRequest filter)
    {
        var query = db.Reports.AsQueryable();

        if (filter.From is not null)
        {
            var from = MonthStart(filter.From);
            query = query.Where(r => r.CreatedAt >= from);
        }

        if (filter.To is not null)
        {
            var before = MonthStart(filter.To).AddMonths(1); // inklusiv bis Monatsende
            query = query.Where(r => r.CreatedAt < before);
        }

        return query;
    }

    private static DateTime MonthStart(string yearMonth) =>
        DateTime.SpecifyKind(new DateTime(int.Parse(yearMonth[..4]), int.Parse(yearMonth[5..]), 1), DateTimeKind.Utc);

    private static string MonthKey(int year, int month) => $"{year:D4}-{month:D2}";

    private static int Count<T>(IEnumerable<T> rows, Func<T, bool> predicate, Func<T, int> count) =>
        rows.Where(predicate).Sum(count);
}
