using Microsoft.EntityFrameworkCore;
using Portal.Core.Logbook;

namespace Portal.Infrastructure.Logbook;

/// <summary>
/// The retention of the Berichtsheft (decision 49): a report is kept for one year (<see cref="LogbookOptions.RetentionDays"/>, counted from the Monday of its
/// week, local time). After that it is deleted with its comments. Before a handed-in report goes, what the evaluation of the year needs is kept as numbers
/// (<see cref="LogbookWeekStatRecord"/>: on time, rating, escalated, answered, done; no content, no text, no person). Reminders of those weeks go as well.
/// A round deals with a limited number of reports, the next round continues.
/// </summary>
public sealed class LogbookRetentionService(PortalDbContext db, LogbookReminderService reminders, TimeProvider time, LogbookOptions options)
{
    public const int BatchSize = 500;

    /// <summary>Deletes the reports that are past their retention; returns how many.</summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, options.Zone).DateTime);
        var cutoff = today.AddDays(-options.RetentionDays);

        var expired = await db.LogbookReports.Where(r => r.Week < cutoff).OrderBy(r => r.Week).Take(BatchSize).ToListAsync(cancellationToken);
        if (expired.Count > 0)
        {
            var settings = await reminders.GetSettingsAsync(cancellationToken);
            var ids = expired.Select(r => r.Id).ToList();
            var answered = (await db.LogbookComments.AsNoTracking()
                    .Where(c => ids.Contains(c.ReportId) && c.Kind == LogbookCommentKind.Reply).Select(c => c.ReportId).Distinct().ToListAsync(cancellationToken))
                .ToHashSet();

            foreach (var report in expired.Where(r => r.Phase == LogbookReportPhase.Submitted))
            {
                await KeepFiguresAsync(report, answered.Contains(report.Id), settings, now, cancellationToken);
            }

            db.LogbookReports.RemoveRange(expired); // the comments go with them (cascade)
        }

        db.LogbookReminders.RemoveRange(await db.LogbookReminders.Where(r => r.Week < cutoff).ToListAsync(cancellationToken));
        await db.SaveChangesAsync(cancellationToken);
        return expired.Count;
    }

    private async Task KeepFiguresAsync(LogbookReportRecord report, bool answered, LogbookReminderSettings settings, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var onTime = report.SubmittedOnTime ?? (report.SubmittedAt is { } at && LogbookEvaluation.IsOnTime(settings, report.Week, at, options.Zone));
        var rating = LogbookRating.ValueOf(LogbookReportService.ReadAnswers(report));
        var row = await db.LogbookWeekStats.FindAsync([report.StoreId, report.Week], cancellationToken);
        if (row is null)
        {
            row = new LogbookWeekStatRecord { StoreId = report.StoreId, Week = report.Week };
            db.LogbookWeekStats.Add(row);
        }

        row.OnTime = onTime;
        row.Rating = rating;
        row.Escalated = report.EscalatedAt is not null;
        row.Answered = answered;
        row.Done = report.State == LogbookReportState.Done;
        row.ArchivedAt = now;
    }
}
