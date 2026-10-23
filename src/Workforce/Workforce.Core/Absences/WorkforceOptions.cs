namespace Workforce.Core.Absences;

public sealed class WorkforceOptions
{
    public const string ConfigurationSection = "Workforce";

    /// <summary>Local time zone: "today" and the end of days are local (summer/winter time included).</summary>
    public string TimeZoneId { get; set; } = "Europe/Berlin";

    /// <summary>Working days of the week (plan: Monday to Saturday, configurable).</summary>
    public List<DayOfWeek> WorkingDays { get; set; } =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday];

    /// <summary>
    /// State (ISO 3166-2:DE code, e.g. <c>NW</c>) for state-specific holidays; <c>null</c> = nationwide holidays only.
    /// TODO(Entscheidung): which state applies (per store? one default?) is open.
    /// </summary>
    public string? HolidayState { get; set; }

    public TimeZoneInfo Zone => TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);

    public WorkingWeek Week => new(WorkingDays.ToHashSet());
}
