namespace Workforce.Infrastructure.Absences;

/// <summary>Vacation entitlement of a person and year, maintained manually.</summary>
/// <summary>Additional vacation days of a person (leftover, special leave) entered by HR; they expire after the first quarter of the following year.</summary>
public sealed class ExtraVacationRecord
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public Workforce.Core.Absences.ExtraVacationKind Kind { get; set; }
    public int Year { get; set; }
    public decimal Days { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>The one row with the expiry rule for additional vacation days that HR has set (no row = they never expire).</summary>
public sealed class VacationRulesRecord
{
    public int Id { get; set; } = 1;
    public int? ExpiryMonth { get; set; }
    public int? ExpiryDay { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
    public Guid ChangedBy { get; set; }
}

public sealed class EntitlementRecord
{
    public Guid EmployeeId { get; set; }
    public int Year { get; set; }
    public decimal Days { get; set; }
}
