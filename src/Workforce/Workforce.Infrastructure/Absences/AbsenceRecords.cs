namespace Workforce.Infrastructure.Absences;

/// <summary>Vacation entitlement of a person and year, maintained manually.</summary>
public sealed class EntitlementRecord
{
    public Guid EmployeeId { get; set; }
    public int Year { get; set; }
    public decimal Days { get; set; }
}
