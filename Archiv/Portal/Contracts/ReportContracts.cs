using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using Portal.Domain.Entities;
using Portal.Domain.Enums;

namespace Portal.Contracts;

public record ReportResponse(
    Guid Id,
    int PersonnelNumber,
    ReportStatus Status,
    ReportSource Source,
    ReportCategory Category,
    string Description,
    ReportSeverity Severity,
    List<LocationResponse> Locations,
    string? Device,
    DateTime? NoticedAt,
    string? NoticedHow,
    string? PriorActivity,
    string? Comment,
    bool Archived,
    Guid? ParentReportId,
    List<Guid> ChildReportIds,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    /// <summary>Setzt die Antwort aus der Datenbankzeile und den Filialen von Rollen-Rechte zusammen (Filialen sortiert nach Store-Code).</summary>
    public static ReportResponse From(ReportRow r, IReadOnlyDictionary<Guid, LocationResponse> locations) => new(
        r.Id, r.PersonnelNumber, r.Status, r.Source, r.Category, r.Description, r.Severity,
        r.LocationIds.Select(id => locations.GetValueOrDefault(id) ?? new LocationResponse(id, string.Empty, string.Empty, false))
            .OrderBy(l => l.StoreCode, StringComparer.Ordinal).ThenBy(l => l.Id).ToList(),
        r.Device, r.NoticedAt, r.NoticedHow, r.PriorActivity, r.Comment, r.Archived, r.ParentReportId, r.ChildReportIds,
        r.CreatedAt, r.UpdatedAt);
}

/// <summary>Die Meldung, wie die Datenbank sie liefert: Filialen nur als IDs (Namen kommen von Rollen-Rechte).</summary>
public record ReportRow(
    Guid Id,
    int PersonnelNumber,
    ReportStatus Status,
    ReportSource Source,
    ReportCategory Category,
    string Description,
    ReportSeverity Severity,
    List<Guid> LocationIds,
    string? Device,
    DateTime? NoticedAt,
    string? NoticedHow,
    string? PriorActivity,
    string? Comment,
    bool Archived,
    Guid? ParentReportId,
    List<Guid> ChildReportIds,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public static readonly Expression<Func<Report, ReportRow>> Projection = r => new ReportRow(
        r.Id, r.PersonnelNumber, r.Status, r.Source, r.Category, r.Description, r.Severity,
        r.ReportLocations.Select(l => l.LocationId).ToList(),
        r.Device, r.NoticedAt, r.NoticedHow, r.PriorActivity, r.Comment, r.Archived, r.ParentReportId,
        r.ChildReports.Select(c => c.Id).ToList(),
        r.CreatedAt, r.UpdatedAt);
}

public record CreateReportRequest
{
    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    [Range(1, int.MaxValue, ErrorMessage = "Dieser Wert muss positiv sein.")]
    public int? PersonnelNumber { get; init; }

    /// <summary>Wird akzeptiert, aber ignoriert: öffentliche Meldungen sind serverseitig immer "employee".</summary>
    public ReportSource? Source { get; init; }

    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    public ReportCategory? Category { get; init; }

    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    public ReportSeverity? Severity { get; init; }

    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    public string? Description { get; init; }

    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    [MinLength(1, ErrorMessage = "Es muss mindestens eine Filiale angegeben werden.")]
    public List<Guid>? LocationIds { get; init; }

    [MaxLength(120, ErrorMessage = "Dieser Wert ist zu lang. Er darf höchstens 120 Zeichen enthalten.")]
    public string? Device { get; init; }

    public DateTime? NoticedAt { get; init; }
    public string? NoticedHow { get; init; }
    public string? PriorActivity { get; init; }
}

/// <summary>PATCH-Semantik: fehlend/null = unverändert.</summary>
public record UpdateReportRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Dieser Wert muss positiv sein.")]
    public int? PersonnelNumber { get; init; }

    public ReportStatus? Status { get; init; }

    /// <summary>Wird akzeptiert, aber ignoriert (nicht änderbar).</summary>
    public ReportSource? Source { get; init; }

    public ReportCategory? Category { get; init; }
    public string? Description { get; init; }
    public ReportSeverity? Severity { get; init; }

    /// <summary>Ersetzt die komplette Filialenliste; eine leere Liste leert sie.</summary>
    public List<Guid>? LocationIds { get; init; }

    [MaxLength(120, ErrorMessage = "Dieser Wert ist zu lang. Er darf höchstens 120 Zeichen enthalten.")]
    public string? Device { get; init; }

    public DateTime? NoticedAt { get; init; }
    public string? NoticedHow { get; init; }
    public string? PriorActivity { get; init; }
    public string? Comment { get; init; }
    public bool? Archived { get; init; }
    public Guid? ParentReportId { get; init; }
}

public class ReportFilterRequest
{
    public ReportStatus? Status { get; init; }
    public ReportCategory? Category { get; init; }
    public ReportSeverity? Severity { get; init; }
    public ReportSource? Source { get; init; }

    /// <summary>Ohne Angabe sind aktive und archivierte Reports enthalten.</summary>
    public bool? Archived { get; init; }

    public string? Search { get; init; }

    [Range(1, int.MaxValue, ErrorMessage = "Dieser Wert muss positiv sein.")]
    public int? PersonnelNumber { get; init; }

    public Guid? LocationId { get; init; }

    /// <summary>Hat Vorrang vor Month.</summary>
    public DateTime? Day { get; init; }

    [RegularExpression(@"^\d{4}-(0[1-9]|1[0-2])$", ErrorMessage = "Format muss YYYY-MM sein.")]
    public string? Month { get; init; }

    [AllowedValues("id", "personnelNumber", "status", "source", "category", "description", "severity", "device",
        "noticedAt", "noticedHow", "priorActivity", "comment", "archived", "createdAt", "updatedAt", "priority", null,
        ErrorMessage = "Ungültiges Sortierfeld.")]
    public string? SortBy { get; init; }

    [RegularExpression("^(asc|desc)$", ErrorMessage = "Erlaubt sind asc oder desc.")]
    public string? SortOrder { get; init; }

    [Range(0, int.MaxValue, ErrorMessage = "Muss mindestens 0 sein.")]
    public int Offset { get; init; }

    [Range(1, 200, ErrorMessage = "Muss zwischen 1 und 200 liegen.")]
    public int Limit { get; init; } = 20;
}
