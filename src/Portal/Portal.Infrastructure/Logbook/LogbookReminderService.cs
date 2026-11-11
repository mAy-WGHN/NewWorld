using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Portal.Core.Logbook;

namespace Portal.Infrastructure.Logbook;

public enum LogbookReminderKind
{
    Unknown = 0,
    Manual = 1,
    Automatic = 2,
}

/// <summary>A reminder that went out (manual or automatic). The automatic ones are unique per store, week and point, so a point is never sent twice.</summary>
public sealed class LogbookReminderRecord
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid StoreId { get; set; }
    public DateOnly Week { get; set; }
    public LogbookReminderKind Kind { get; set; }

    /// <summary>The point of the plan (automatic reminders only).</summary>
    public int? PointIndex { get; set; }

    public DateTimeOffset SentAt { get; set; }
    public Guid? SentBy { get; set; }
}

/// <summary>The one row with the settings of the automatic reminder (maintained by the holders of <c>logbook:manage-reminders</c>).</summary>
public sealed class LogbookReminderSettingsRecord
{
    public int Id { get; set; } = 1;
    public bool Enabled { get; set; }
    public DateTimeOffset? EnabledAt { get; set; }
    public DayOfWeek DueDay { get; set; }
    public TimeOnly DueTime { get; set; }
    public required string PointsJson { get; set; }
    public int MaxReminders { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
    public Guid? ChangedBy { get; set; }
}

public enum LogbookReminderFailure
{
    None = 0,
    FutureWeek = 1,
    WeekTooOld = 2,
    NotFound = 3,

    /// <summary>The report of the week is already handed in: nothing to remind.</summary>
    AlreadySubmitted = 4,

    /// <summary>There was a reminder a short time ago (manual reminders are limited so that nobody is pestered).</summary>
    TooSoon = 5,

    /// <summary>Nobody holds the write privilege for the store: the reminder would go nowhere.</summary>
    NoStoreManagers = 6,
}

public sealed record LogbookReminderResult(LogbookReminderFailure Failure, LogbookReminderRecord? Reminder = null);

/// <summary>A store of the reach without a handed-in report for the week, with what is known about reminders.</summary>
public sealed record LogbookMissingStore(Guid StoreId, int RemindersSent, DateTimeOffset? LastReminderAt, bool HasStoreManagers, bool ReportStarted);

/// <summary>
/// Reminding the store managers of the weekly report (decision 49): manually by a leader of the store, and automatically by the background service
/// according to the settings that the holders of <c>logbook:manage-reminders</c> maintain. Both count together towards the maximum per store and week.
/// </summary>
public sealed class LogbookReminderService(
    PortalDbContext db, LogbookStructureStore structures, LogbookNotifier notifier, LogbookReportService reports, TimeProvider time, LogbookOptions options)
{
    /// <summary>A manual reminder for the same store and week at most every 12 hours.</summary>
    public static readonly TimeSpan MinManualInterval = TimeSpan.FromHours(12);

    private static readonly JsonSerializerOptions Json = new();

    public async Task<LogbookStructure> StructureAsync(CancellationToken cancellationToken) => await structures.LoadAsync(cancellationToken);

    // ------------------------------------------------------------------ settings

    public async Task<LogbookReminderSettings> GetSettingsAsync(CancellationToken cancellationToken) =>
        await db.LogbookReminderSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken) is { } row ? ToSettings(row) : LogbookReminderSettings.Default;

    public async Task<LogbookReminderSettings> SaveSettingsAsync(LogbookReminderSettings wanted, Guid by, CancellationToken cancellationToken)
    {
        var row = await db.LogbookReminderSettings.FirstOrDefaultAsync(cancellationToken);
        var now = time.GetUtcNow();
        var applied = LogbookReminderPlan.Apply(row is null ? null : ToSettings(row), wanted, now);
        if (row is null)
        {
            row = new LogbookReminderSettingsRecord { PointsJson = "[]" };
            db.LogbookReminderSettings.Add(row);
        }

        Fill(row, applied, now, by);
        await db.SaveChangesAsync(cancellationToken);
        return ToSettings(row);
    }

    // ------------------------------------------------------------------ manual

    /// <summary>A leader reminds the store managers of a store that has not handed in the report of the week.</summary>
    public async Task<LogbookReminderResult> RemindAsync(Guid storeId, DateOnly day, Guid by, CancellationToken cancellationToken)
    {
        switch (reports.CheckWeek(day))
        {
            case LogbookWeekError.InFuture:
                return new(LogbookReminderFailure.FutureWeek);
            case LogbookWeekError.TooOld:
                return new(LogbookReminderFailure.WeekTooOld);
        }

        var week = LogbookWeek.StartOf(day);
        if (await db.LogbookReports.AnyAsync(r => r.StoreId == storeId && r.Week == week && r.Phase == LogbookReportPhase.Submitted, cancellationToken))
        {
            return new(LogbookReminderFailure.AlreadySubmitted);
        }

        var now = time.GetUtcNow();
        var since = now - MinManualInterval;
        if (await db.LogbookReminders.AnyAsync(r => r.StoreId == storeId && r.Week == week && r.Kind == LogbookReminderKind.Manual && r.SentAt > since, cancellationToken))
        {
            return new(LogbookReminderFailure.TooSoon);
        }

        var reminder = new LogbookReminderRecord { StoreId = storeId, Week = week, Kind = LogbookReminderKind.Manual, SentAt = now, SentBy = by };
        if (!notifier.ReminderSent(storeId, $"{reminder.Id}", by, reminder.Id))
        {
            return new(LogbookReminderFailure.NoStoreManagers);
        }

        db.LogbookReminders.Add(reminder);
        await db.SaveChangesAsync(cancellationToken);
        return new(LogbookReminderFailure.None, reminder);
    }

    /// <summary>The stores of the person's reach that have not handed in the report of the week.</summary>
    public async Task<IReadOnlyList<LogbookMissingStore>> MissingAsync(Guid person, DateOnly day, CancellationToken cancellationToken)
    {
        var week = LogbookWeek.StartOf(day);
        var structure = await structures.LoadAsync(cancellationToken);
        var ids = LogbookReach.Of(structure, person).StoreIds.ToList();
        var reportsOfWeek = await db.LogbookReports.AsNoTracking().Where(r => r.Week == week && ids.Contains(r.StoreId)).ToListAsync(cancellationToken);
        var sent = await db.LogbookReminders.AsNoTracking().Where(r => r.Week == week && ids.Contains(r.StoreId)).ToListAsync(cancellationToken);
        return [.. ids
            .Where(id => reportsOfWeek.All(r => r.StoreId != id || r.Phase != LogbookReportPhase.Submitted))
            .Select(id =>
            {
                var mine = sent.Where(r => r.StoreId == id).ToList();
                return new LogbookMissingStore(id, mine.Count, mine.Count == 0 ? null : mine.Max(r => r.SentAt), notifier.StoreManagersOf(id).Count > 0, reportsOfWeek.Any(r => r.StoreId == id));
            })];
    }

    // ------------------------------------------------------------------ automatic

    /// <summary>One round of the background service: sends the reminders that are due. Returns how many went out.</summary>
    public async Task<int> RunAutomaticAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var row = await db.LogbookReminderSettings.FirstOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            // The first round: the reminder starts now, earlier weeks are not reminded (otherwise the go-live would remind everybody for weeks long over).
            await SaveSettingsAsync(LogbookReminderSettings.Default, Guid.Empty, cancellationToken);
            row = await db.LogbookReminderSettings.AsNoTracking().FirstAsync(cancellationToken);
        }

        var settings = ToSettings(row);
        if (!settings.Enabled || settings.Points.Count == 0)
        {
            return 0;
        }

        var structure = await structures.LoadAsync(cancellationToken);
        var stores = structure.Stores.Select(s => s.StoreId).Where(id => LogbookReach.CanReceiveReports(structure, id)).ToList();
        var sentCount = 0;
        foreach (var week in LogbookReminderPlan.WeeksToCheck(settings, reports.CurrentWeek))
        {
            var handedIn = (await db.LogbookReports.AsNoTracking().Where(r => r.Week == week && r.Phase == LogbookReportPhase.Submitted).Select(r => r.StoreId).ToListAsync(cancellationToken)).ToHashSet();
            var already = await db.LogbookReminders.AsNoTracking().Where(r => r.Week == week).ToListAsync(cancellationToken);
            foreach (var storeId in stores.Where(id => !handedIn.Contains(id)))
            {
                var mine = already.Where(r => r.StoreId == storeId).ToList();
                var lastPoint = mine.Where(r => r.PointIndex is not null).Select(r => r.PointIndex).Max();
                if (LogbookReminderPlan.DuePoint(settings, week, now, options.Zone, lastPoint, mine.Count) is not { } point)
                {
                    continue;
                }

                var reminder = new LogbookReminderRecord { StoreId = storeId, Week = week, Kind = LogbookReminderKind.Automatic, PointIndex = point, SentAt = now };
                if (!notifier.ReminderSent(storeId, $"{storeId}:{week:yyyy-MM-dd}:{point}", null, reminder.Id))
                {
                    continue; // nobody to remind: nothing is marked, the next round looks again
                }

                db.LogbookReminders.Add(reminder);
                sentCount++;
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        return sentCount;
    }

    // ------------------------------------------------------------------ mapping

    private static LogbookReminderSettings ToSettings(LogbookReminderSettingsRecord row) => new(
        row.Enabled, row.EnabledAt, row.DueDay, row.DueTime,
        JsonSerializer.Deserialize<List<LogbookReminderPoint>>(row.PointsJson, Json) ?? [], row.MaxReminders);

    private static void Fill(LogbookReminderSettingsRecord row, LogbookReminderSettings settings, DateTimeOffset now, Guid by)
    {
        row.Enabled = settings.Enabled;
        row.EnabledAt = settings.EnabledAt;
        row.DueDay = settings.DueDay;
        row.DueTime = settings.DueTime;
        row.PointsJson = JsonSerializer.Serialize(settings.Points.OrderBy(p => p.DaysAfterDue).ThenBy(p => p.Time).ToList(), Json);
        row.MaxReminders = settings.MaxReminders;
        row.ChangedAt = now;
        row.ChangedBy = by == Guid.Empty ? null : by;
    }
}
