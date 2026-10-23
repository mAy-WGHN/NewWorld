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
    /// State (ISO 3166-2:DE code) for state-specific holidays; <c>null</c> = nationwide holidays only. Decision 2026-10-23:
    /// Bavaria for all stores; the municipal holiday of Augsburg follows the city of the store address.
    /// </summary>
    public string? HolidayState { get; set; } = "BY";

    public TimeZoneInfo Zone => TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);

    public WorkingWeek Week => new(WorkingDays.ToHashSet());
}
