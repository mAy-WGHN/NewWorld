using Portal.Core.Reports;

namespace Portal.Infrastructure.Reports;

/// <summary>
/// A report (incident report). Contents are class 2 (sensitive facts): they never go onto the bus and never into a notification text.
/// Times are UTC without zone, <see cref="NoticedAt"/> is the wall-clock time the client entered (kept as sent, not converted).
/// </summary>
public sealed class ReportRecord
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public int PersonnelNumber { get; set; }
    public ReportStatus Status { get; set; } = ReportStatus.Open;
    public ReportSource Source { get; set; }
    public ReportCategory Category { get; set; }
    public required string Description { get; set; }
    public ReportSeverity Severity { get; set; }
    public string? Device { get; set; }
    public DateTime? NoticedAt { get; set; }
    public string? NoticedHow { get; set; }
    public string? PriorActivity { get; set; }

    /// <summary>Only by the administration, never from the public form.</summary>
    public string? Comment { get; set; }

    /// <summary>Soft delete.</summary>
    public bool Archived { get; set; }

    public Guid? ParentReportId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<ReportLocationRecord> Locations { get; set; } = [];
}

/// <summary>Report ↔ store. The stores belong to BC (copy in <c>stores</c>); no foreign key, so a store that is archived later does not touch old reports.</summary>
public sealed class ReportLocationRecord
{
    public Guid ReportId { get; set; }
    public Guid LocationId { get; set; }
}

/// <summary>Who is told about new reports of a category (maintained by the administration).</summary>
public sealed class ReportRecipientRecord
{
    public ReportCategory Category { get; set; }
    public Guid EmployeeId { get; set; }
}
