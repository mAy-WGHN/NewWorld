namespace Portal.Core.Logbook;

/// <summary>Settings of the Berichtsheft (section <c>Portal:Logbook</c>).</summary>
public sealed class LogbookOptions
{
    public const string ConfigurationSection = "Portal:Logbook";

    /// <summary>The zone in which "this week" and "today" count (the week changes at local midnight).</summary>
    public string TimeZoneId { get; set; } = "Europe/Berlin";

    public TimeZoneInfo Zone => TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);

    /// <summary>How long a report is kept: reports of weeks that began longer ago than this are deleted, what remains is the figures for the evaluation (no content, no persons).</summary>
    public int RetentionDays { get; set; } = 365;
}

/// <summary>Why a week cannot be written.</summary>
public enum LogbookWeekError
{
    None = 0,
    InFuture = 1,
    TooOld = 2,
}

/// <summary>
/// The week of a weekly report: it starts on Monday, identified by its Monday. A report can be written for the current week and up to
/// <see cref="MaxWeeksBack"/> weeks back (a late report is better than none), never for the future.
/// </summary>
public static class LogbookWeek
{
    public const int MaxWeeksBack = 8;

    /// <summary>The Monday of the week the day belongs to.</summary>
    public static DateOnly StartOf(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    /// <summary>The Monday of the current week in the given zone (the day changes at local midnight, also on the days of the clock change).</summary>
    public static DateOnly Current(DateTimeOffset now, TimeZoneInfo zone) => StartOf(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime));

    /// <summary>Whether the week of <paramref name="day"/> can be written, given the Monday of the current week.</summary>
    public static LogbookWeekError Check(DateOnly day, DateOnly currentWeek)
    {
        var week = StartOf(day);
        return week > currentWeek
            ? LogbookWeekError.InFuture
            : week < currentWeek.AddDays(-7 * MaxWeeksBack) ? LogbookWeekError.TooOld : LogbookWeekError.None;
    }
}

/// <summary>Where a report is: still being written, or handed in.</summary>
public enum LogbookReportPhase
{
    Unknown = 0,
    Draft = 1,
    Submitted = 2,
}

/// <summary>How far the follow-up of a handed-in report is: new, in progress (regional leader), followed up (store manager), done (regional leader).</summary>
public enum LogbookReportState
{
    Unknown = 0,
    New = 1,
    InProgress = 2,
    FollowedUp = 3,
    Done = 4,
}
