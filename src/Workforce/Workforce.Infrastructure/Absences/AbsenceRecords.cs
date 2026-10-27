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

public sealed class EntitlementRecord
{
    public Guid EmployeeId { get; set; }
    public int Year { get; set; }
    public decimal Days { get; set; }
}
