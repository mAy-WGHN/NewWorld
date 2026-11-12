namespace Access.Core;

/// <summary>
/// Seed for the local landscape: <see cref="RoleFile"/> is the role file of the repository (<c>docs/rollen.json</c>), <see cref="HrPersonnelNumbers"/> are the
/// people who become the HR team (role <see cref="HrRoleName"/>, responsible for everybody on the HR topics). Empty = no seed.
/// </summary>
public sealed class DevelopmentSeedOptions
{
    public string? RoleFile { get; set; }

    public List<string> HrPersonnelNumbers { get; set; } = [];

    public string HrRoleName { get; set; } = "Personalabteilung";

    /// <summary>
    /// Demo people with one role each, as <c>personnelNumber:role</c> (reach everywhere) or <c>personnelNumber:role:home</c> (reach only the main store),
    /// so the local landscape can be shown without the administrator role.
    /// </summary>
    public List<string> Assignments { get; set; } = [];

    public bool IsActive => !string.IsNullOrWhiteSpace(RoleFile) || HrPersonnelNumbers.Count > 0 || Assignments.Count > 0;
}

public sealed class AccessOptions
{
    public const string ConfigurationSection = "Access";

    /// <summary>Local time zone of the business: exit dates end at local midnight (summer/winter time included).</summary>
    public string TimeZoneId { get; set; } = "Europe/Berlin";

    /// <summary>How often permissions are recalculated so that time-limited assignments start and end on time.</summary>
    public TimeSpan RecalculationInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Personnel numbers that get the built-in administrator role permanently once their copy arrives (first administrators).</summary>
    public List<string> BootstrapAdministrators { get; set; } = [];

    /// <summary>Local development only (set by the AppHost, never in another environment): usable roles and an HR team at the first start.</summary>
    public DevelopmentSeedOptions DevelopmentSeed { get; set; } = new();

    public TimeZoneInfo Zone => TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
}
