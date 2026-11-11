namespace Portal.Core.Logbook;

/// <summary>A reminder point: a number of days after the due day, at a time of day (local time).</summary>
public sealed record LogbookReminderPoint(int DaysAfterDue, TimeOnly Time);

/// <summary>
/// The settings of the automatic reminder (decision 49), maintained in the Portal by the holders of <c>logbook:manage-reminders</c>: on or off, the due
/// day and time of the report of a week, the reminder points counted from the due day, and the maximum number of reminders per store and week (manual
/// ones count with). <see cref="EnabledAt"/> is the time it was switched on: points before it do not count, so switching it on never sends a burst for
/// weeks that are long over.
/// </summary>
public sealed record LogbookReminderSettings(bool Enabled, DateTimeOffset? EnabledAt, DayOfWeek DueDay, TimeOnly DueTime, IReadOnlyList<LogbookReminderPoint> Points, int MaxReminders)
{
    /// <summary>The start values: due on Friday at 18:00, reminded on Friday at noon and on Monday at 09:00, at most twice.</summary>
    public static LogbookReminderSettings Default { get; } = new(
        true, null, DayOfWeek.Friday, new TimeOnly(18, 0), [new(0, new TimeOnly(12, 0)), new(3, new TimeOnly(9, 0))], 2);
}

public enum LogbookReminderError
{
    Unknown = 0,
    TooManyPoints = 1,
    DaysOutOfRange = 2,
    DuplicatePoint = 3,
    MaxOutOfRange = 4,
    DueDayInvalid = 5,
}

/// <summary>The rules for the settings: at most 10 points, 0 to 14 days after the due day, no point twice, a maximum from 1 to 10, a real weekday.</summary>
public static class LogbookReminderValidator
{
    public const int MaxPoints = 10;
    public const int MaxDays = 14;
    public const int MaxRemindersLimit = 10;

    public static IReadOnlyList<LogbookReminderError> Validate(LogbookReminderSettings settings)
    {
        var errors = new List<LogbookReminderError>();
        if (!Enum.IsDefined(settings.DueDay))
        {
            errors.Add(LogbookReminderError.DueDayInvalid);
        }

        if (settings.Points.Count > MaxPoints)
        {
            errors.Add(LogbookReminderError.TooManyPoints);
        }

        if (settings.Points.Any(p => p.DaysAfterDue is < 0 or > MaxDays))
        {
            errors.Add(LogbookReminderError.DaysOutOfRange);
        }

        if (settings.Points.GroupBy(p => (p.DaysAfterDue, p.Time)).Any(g => g.Count() > 1))
        {
            errors.Add(LogbookReminderError.DuplicatePoint);
        }

        if (settings.MaxReminders is < 1 or > MaxRemindersLimit)
        {
            errors.Add(LogbookReminderError.MaxOutOfRange);
        }

        return errors;
    }
}

/// <summary>
/// When a reminder is due for the report of a week. The points are taken in time order; a point is due once its time has come (local time, also on the days
/// of the clock change); of several overdue points only the last one sends (no burst after a standstill); nothing is sent after the maximum, while switched
/// off, or for points before the reminder was switched on.
/// </summary>
public static class LogbookReminderPlan
{
    /// <summary>The points in time order (the index of a point always means this order).</summary>
    public static IReadOnlyList<LogbookReminderPoint> Ordered(LogbookReminderSettings settings) => [.. settings.Points.OrderBy(p => p.DaysAfterDue).ThenBy(p => p.Time)];

    /// <summary>When the point (index in <see cref="Ordered"/>) of the week is due. A time that does not exist (clock goes forward) moves to the hour after, one that exists twice counts at the later instant.</summary>
    public static DateTimeOffset ScheduledAt(LogbookReminderSettings settings, DateOnly week, int point, TimeZoneInfo zone)
    {
        var p = Ordered(settings)[point];
        var dueOffset = ((int)settings.DueDay + 6) % 7; // Monday = 0
        var local = week.AddDays(dueOffset + p.DaysAfterDue).ToDateTime(p.Time);
        if (zone.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }

    /// <summary>
    /// The index of the point to send now for the week, or <c>null</c>. <paramref name="lastSentPoint"/> is the highest point already sent for this store and
    /// week (automatic), if any; <paramref name="sentCount"/> how many reminders (automatic and manual) went out for this store and week.
    /// </summary>
    public static int? DuePoint(LogbookReminderSettings settings, DateOnly week, DateTimeOffset now, TimeZoneInfo zone, int? lastSentPoint, int sentCount)
    {
        if (!settings.Enabled || sentCount >= settings.MaxReminders)
        {
            return null;
        }

        int? due = null;
        for (var i = (lastSentPoint ?? -1) + 1; i < settings.Points.Count; i++)
        {
            var at = ScheduledAt(settings, week, i, zone);
            if (at <= now && (settings.EnabledAt is not { } since || at >= since))
            {
                due = i;
            }
        }

        return due;
    }

    /// <summary>The weeks that can still have a due point: the current one and as many back as the last point reaches (a point after the weekend belongs to the next week).</summary>
    public static IReadOnlyList<DateOnly> WeeksToCheck(LogbookReminderSettings settings, DateOnly currentWeek)
    {
        var dueOffset = ((int)settings.DueDay + 6) % 7;
        var reach = settings.Points.Count == 0 ? 0 : (dueOffset + settings.Points.Max(p => p.DaysAfterDue)) / 7;
        return [.. Enumerable.Range(0, reach + 1).Select(i => currentWeek.AddDays(-7 * i))];
    }

    /// <summary>The settings to save: switching on (or the first time) sets <see cref="LogbookReminderSettings.EnabledAt"/> to now; anything else keeps the time.</summary>
    public static LogbookReminderSettings Apply(LogbookReminderSettings? current, LogbookReminderSettings wanted, DateTimeOffset now) =>
        wanted.Enabled && (current is null || !current.Enabled)
            ? wanted with { EnabledAt = now }
            : wanted with { EnabledAt = current?.EnabledAt ?? wanted.EnabledAt };
}
