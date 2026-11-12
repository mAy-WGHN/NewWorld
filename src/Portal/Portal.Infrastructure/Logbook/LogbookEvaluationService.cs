using Microsoft.EntityFrameworkCore;
using Portal.Core.Logbook;

namespace Portal.Infrastructure.Logbook;

/// <summary>
/// What is kept of a handed-in report when the report itself is deleted after its retention: numbers and flags only, no content, no text and no person
/// (decision 49), one row per store and week. The evaluation of a year reads the reports that still exist and these rows for the ones that are gone.
/// </summary>
public sealed class LogbookWeekStatRecord
{
    public Guid StoreId { get; set; }
    public DateOnly Week { get; set; }
    public bool OnTime { get; set; }

    /// <summary>The overall rating 1 to 5 (<c>null</c> for a report of a template version without it).</summary>
    public int? Rating { get; set; }

    public bool Escalated { get; set; }
    public bool Answered { get; set; }
    public bool Done { get; set; }
    public DateTimeOffset ArchivedAt { get; set; }
}

/// <summary>The figures of a store for a year.</summary>
public sealed record LogbookStoreFigures(Guid StoreId, LogbookFigures Figures);

/// <summary>
/// The evaluation of a year (decision 49): per store, from the reports that still exist (live) and the figures kept of deleted ones (archived). A week
/// belongs to the year of its Monday. Whether a report was on time was decided when it was handed in; what is missing counts every due week of the year
/// without a handed-in report for the stores as they are in the hierarchy now.
/// </summary>
public sealed class LogbookEvaluationService(PortalDbContext db, LogbookReminderService reminders, TimeProvider time, LogbookOptions options)
{
    public async Task<IReadOnlyList<LogbookStoreFigures>> YearAsync(int year, IReadOnlyCollection<Guid> storeIds, CancellationToken cancellationToken)
    {
        var settings = await reminders.GetSettingsAsync(cancellationToken);
        var overdue = LogbookEvaluation.OverdueWeeks(year, time.GetUtcNow(), settings, options.Zone);
        var figures = (await WeeksAsync(year, storeIds, settings, cancellationToken)).ToLookup(f => f.StoreId);
        return [.. storeIds.Select(id => new LogbookStoreFigures(id, LogbookEvaluation.ForStore([.. figures[id]], overdue)))];
    }

    /// <summary>The handed-in reports of the stores in the year: the ones that still exist, and for a week without one the figures that were kept.</summary>
    private async Task<IReadOnlyList<LogbookWeekFigures>> WeeksAsync(int year, IReadOnlyCollection<Guid> storeIds, LogbookReminderSettings settings, CancellationToken cancellationToken)
    {
        var first = new DateOnly(year, 1, 1);
        var last = new DateOnly(year, 12, 31);
        var live = await db.LogbookReports.AsNoTracking()
            .Where(r => storeIds.Contains(r.StoreId) && r.Phase == LogbookReportPhase.Submitted && r.Week >= first && r.Week <= last)
            .Select(r => new
            {
                r.StoreId, r.Week, r.AnswersJson, r.SubmittedAt, r.SubmittedOnTime, r.State, r.EscalatedAt,
                Answered = db.LogbookComments.Any(c => c.ReportId == r.Id && c.Kind == LogbookCommentKind.Reply),
            })
            .ToListAsync(cancellationToken);

        var weeks = live
            .Select(r => new LogbookWeekFigures(
                r.StoreId, r.Week,
                r.SubmittedOnTime ?? (r.SubmittedAt is { } at && LogbookEvaluation.IsOnTime(settings, r.Week, at, options.Zone)),
                LogbookRating.ValueOf(LogbookReportService.ReadAnswers(new LogbookReportRecord { AnswersJson = r.AnswersJson })),
                r.EscalatedAt is not null, r.Answered, r.State == LogbookReportState.Done))
            .ToList();

        var kept = weeks.Select(w => (w.StoreId, w.Week)).ToHashSet();
        var archived = await db.LogbookWeekStats.AsNoTracking()
            .Where(s => storeIds.Contains(s.StoreId) && s.Week >= first && s.Week <= last)
            .ToListAsync(cancellationToken);
        weeks.AddRange(archived.Where(s => !kept.Contains((s.StoreId, s.Week))).Select(s => new LogbookWeekFigures(s.StoreId, s.Week, s.OnTime, s.Rating, s.Escalated, s.Answered, s.Done)));
        return weeks;
    }
}
