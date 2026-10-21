using Portal.Authentication;
using Portal.Domain.Enums;

namespace Portal.Domain.Entities;

[PrivilegeResource("Meldungen", Actions = CrudAction.Read | CrudAction.Update | CrudAction.Delete, DeleteLabel = "Archivieren", ScopedActions = CrudAction.Read | CrudAction.Update | CrudAction.Delete)] // Anlegen ist öffentlich (POST /api/reports), daher kein "create"
public class Report
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public int PersonnelNumber { get; set; }
    public ReportStatus Status { get; set; } = ReportStatus.Open;

    /// <summary>Nach Erstellung nicht änderbar.</summary>
    public ReportSource Source { get; private set; }

    public ReportCategory Category { get; set; }
    public string Description { get; set; } = string.Empty;
    public ReportSeverity Severity { get; set; }
    public string? Device { get; set; }
    public DateTime? NoticedAt { get; set; }
    public string? NoticedHow { get; set; }
    public string? PriorActivity { get; set; }

    /// <summary>Nur Admin, nie aus dem öffentlichen Formular.</summary>
    public string? Comment { get; set; }

    public bool Archived { get; set; }
    public Guid? ParentReportId { get; set; }
    public Report? ParentReport { get; set; }
    public ICollection<Report> ChildReports { get; } = [];
    public ICollection<ReportLocation> ReportLocations { get; } = [];
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    private Report() { }

    public Report(int personnelNumber, ReportSource source, ReportCategory category,
        ReportSeverity severity, string description, IEnumerable<Guid> locationIds)
    {
        PersonnelNumber = personnelNumber;
        Source = source;
        Category = category;
        Severity = severity;
        Description = description;
        foreach (var locationId in locationIds) AddLocation(locationId);
    }

    public IEnumerable<Guid> LocationIds => ReportLocations.Select(l => l.LocationId);

    public void AddLocation(Guid locationId)
    {
        if (ReportLocations.All(l => l.LocationId != locationId)) ReportLocations.Add(new ReportLocation { ReportId = Id, LocationId = locationId });
    }
}
