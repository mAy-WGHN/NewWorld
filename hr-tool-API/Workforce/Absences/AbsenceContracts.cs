using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using HrTool_API.Common.Api;
using HrTool_API.Domain.Entities;

namespace HrTool_API.Workforce;

/// <summary>Eine Nicht-Verfügbarkeit mit Mitarbeiter und Art (Nummer, Name, Code, Kürzel), wie sie das Urlaubscockpit und die Abwesenheitsliste zeigen.</summary>
public record AbsenceResponse(
    Guid Id, Guid EmployeeId, string EmployeeNo, string FirstName, string LastName,
    Guid AbsenceTypeId, string AbsenceTypeCode, string? ScheduleSymbol, bool RequiresApproval,
    DateOnly DateFrom, DateOnly DateTo, decimal Days, decimal VacationDays, TimeOnly? TimeFrom, TimeOnly? TimeTo,
    string? Remark, string? ApprovalStatus, string? RejectionReason, DateTime? RequestedAt, bool Confirmed, string? RecordedBy,
    DateTime CreatedAt, DateTime UpdatedAt)
{
    public static readonly Expression<Func<Absence, AbsenceResponse>> Projection = a => new AbsenceResponse(
        a.Id, a.EmployeeId, a.Employee!.EmployeeNo, a.Employee.FirstName, a.Employee.LastName,
        a.AbsenceTypeId, a.AbsenceType!.Code, a.AbsenceType.ScheduleSymbol, a.AbsenceType.RequiresApproval,
        a.DateFrom, a.DateTo, a.Days, a.VacationDays, a.TimeFrom, a.TimeTo,
        a.Remark, a.ApprovalStatus, a.RejectionReason, a.RequestedAt, a.Confirmed, a.RecordedBy,
        a.CreatedAt, a.UpdatedAt);
}

public class AbsenceFilterRequest : PaginatedFilterRequest
{
    public Guid? EmployeeId { get; init; }
    public Guid? AbsenceTypeId { get; init; }
    public Guid? WorkplaceId { get; init; }

    /// <summary>Alle Arbeitsorte dieser Filiale (Rollen-Rechte).</summary>
    public Guid? LocationId { get; init; }

    /// <summary>Zeitraum: liefert alle Abwesenheiten, die ihn berühren.</summary>
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }

    /// <summary>Genauer Genehmigungsstatus (Text aus BC, z. B. <c>Vorgesetzter akzeptiert</c>).</summary>
    [MaxLength(50, ErrorMessage = "Dieser Wert ist zu lang. Er darf höchstens 50 Zeichen enthalten.")]
    public string? ApprovalStatus { get; init; }
}

public record CreateAbsenceRequest
{
    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    public Guid? EmployeeId { get; init; }

    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    public Guid? AbsenceTypeId { get; init; }

    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    public DateOnly? DateFrom { get; init; }

    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    public DateOnly? DateTo { get; init; }

    /// <summary>„Tage“ / „Arbeitstage (berechnet)“; BC rechnet sie (Wochenende zählt, Feiertage nicht) und sie werden übernommen, nicht selbst gerechnet.</summary>
    [Range(0, 366, ErrorMessage = "Die Tage müssen zwischen 0 und 366 liegen.")]
    public decimal? Days { get; init; }

    [Range(0, 366, ErrorMessage = "Die Urlaubstage müssen zwischen 0 und 366 liegen.")]
    public decimal? VacationDays { get; init; }

    /// <summary>Nur bei stundenweiser Nicht-Verfügbarkeit; Beginn und Ende gelten nur zusammen.</summary>
    public TimeOnly? TimeFrom { get; init; }
    public TimeOnly? TimeTo { get; init; }

    public string? Remark { get; init; }

    /// <summary>Nur bei Arten, die genehmigt werden müssen (Urlaub).</summary>
    [MaxLength(50, ErrorMessage = "Dieser Wert ist zu lang. Er darf höchstens 50 Zeichen enthalten.")]
    public string? ApprovalStatus { get; init; }

    /// <summary>„Beantragt am“; bei Urlaubsanträgen ohne Angabe der Zeitpunkt des Anlegens.</summary>
    public DateTime? RequestedAt { get; init; }

    public bool Confirmed { get; init; }
}

/// <summary>PATCH: fehlend/<c>null</c> = unverändert. Mitarbeiter und Art ändern sich nicht (löschen und neu anlegen).</summary>
public record UpdateAbsenceRequest
{
    public DateOnly? DateFrom { get; init; }
    public DateOnly? DateTo { get; init; }

    [Range(0, 366, ErrorMessage = "Die Tage müssen zwischen 0 und 366 liegen.")]
    public decimal? Days { get; init; }

    [Range(0, 366, ErrorMessage = "Die Urlaubstage müssen zwischen 0 und 366 liegen.")]
    public decimal? VacationDays { get; init; }

    public TimeOnly? TimeFrom { get; init; }
    public TimeOnly? TimeTo { get; init; }
    public string? Remark { get; init; }

    /// <summary>Genehmigungsstatus; nur bei Urlaub. Entscheidet HR (genehmigen/ablehnen), steht hier der neue Wert.</summary>
    [MaxLength(50, ErrorMessage = "Dieser Wert ist zu lang. Er darf höchstens 50 Zeichen enthalten.")]
    public string? ApprovalStatus { get; init; }

    public string? RejectionReason { get; init; }
    public bool? Confirmed { get; init; }
}
