namespace Access.Core;

public sealed class AccessOptions
{
    public const string ConfigurationSection = "Access";

    /// <summary>Local time zone of the business: exit dates end at local midnight (summer/winter time included).</summary>
    public string TimeZoneId { get; set; } = "Europe/Berlin";

    /// <summary>How often permissions are recalculated so that time-limited assignments start and end on time.</summary>
    public TimeSpan RecalculationInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Personnel numbers that get the built-in administrator role permanently once their copy arrives (first administrators).</summary>
    public List<string> BootstrapAdministrators { get; set; } = [];

    public TimeZoneInfo Zone => TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
}
