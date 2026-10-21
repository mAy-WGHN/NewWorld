using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using HrTool_API.Common.Api;
using HrTool_API.Domain.Entities;

namespace HrTool_API.Workforce;

public record ScheduleResponse(
    Guid Id, string Number, Guid WorkplaceId, string WorkplaceCode, Guid? LocationId, int Year, int Month, ScheduleStatus Status, decimal BudgetHours, DateTime CreatedAt, DateTime UpdatedAt)
{
    public static readonly Expression<Func<Schedule, ScheduleResponse>> Projection =
        s => new ScheduleResponse(s.Id, s.Number, s.WorkplaceId, s.Workplace!.Code, s.Workplace.LocationId, s.Year, s.Month, s.Status, s.BudgetHours, s.CreatedAt, s.UpdatedAt);

    public static ScheduleResponse From(Schedule s) => new(s.Id, s.Number, s.WorkplaceId, s.Workplace!.Code, s.Workplace.LocationId, s.Year, s.Month, s.Status, s.BudgetHours, s.CreatedAt, s.UpdatedAt);
}

/// <summary>Die Mitarbeiter-ID genügt: Namen und Personalnummer holt man über die Mitarbeiter-Routen (mit deren Rechten und Scope).</summary>
public record ShiftResponse(
    Guid Id, Guid ScheduleId, Guid EmployeeId, DateOnly Date, TimeOnly? StartTime, TimeOnly? EndTime,
    string? ShiftCode, Guid? PlanningRoleId, Guid? AbsenceTypeId, string? Remark)
{
    public static readonly Expression<Func<Shift, ShiftResponse>> Projection =
        s => new ShiftResponse(s.Id, s.ScheduleId, s.EmployeeId, s.Date, s.StartTime, s.EndTime, s.ShiftCode, s.PlanningRoleId, s.AbsenceTypeId, s.Remark);

    public static ShiftResponse From(Shift s) => new(s.Id, s.ScheduleId, s.EmployeeId, s.Date, s.StartTime, s.EndTime, s.ShiftCode, s.PlanningRoleId, s.AbsenceTypeId, s.Remark);
}

public class ScheduleFilterRequest : PaginatedFilterRequest
{
    public Guid? WorkplaceId { get; init; }

    /// <summary>Alle Arbeitsorte dieser Filiale (Rollen-Rechte).</summary>
    public Guid? LocationId { get; init; }

    [Range(2000, 2100, ErrorMessage = "Das Jahr muss zwischen 2000 und 2100 liegen.")]
    public int? Year { get; init; }

    [Range(1, 12, ErrorMessage = "Der Monat muss zwischen 1 und 12 liegen.")]
    public int? Month { get; init; }

    public ScheduleStatus? Status { get; init; }
}

public record CreateScheduleRequest
{
    /// <summary>Plannummer aus BC (z. B. <c>DPAB0000039</c>).</summary>
    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    [MaxLength(20, ErrorMessage = "Dieser Wert ist zu lang. Er darf höchstens 20 Zeichen enthalten.")]
    public string? Number { get; init; }

    /// <summary>Arbeitsort (<c>GET /api/workplaces</c>, z. B. <c>AH</c>); die Filiale für den Scope ergibt sich daraus.</summary>
    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    public Guid? WorkplaceId { get; init; }

    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    [Range(2000, 2100, ErrorMessage = "Das Jahr muss zwischen 2000 und 2100 liegen.")]
    public int? Year { get; init; }

    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    [Range(1, 12, ErrorMessage = "Der Monat muss zwischen 1 und 12 liegen.")]
    public int? Month { get; init; }

    /// <summary>Standard: Entwurf.</summary>
    public ScheduleStatus? Status { get; init; }

    [Range(0, 999999, ErrorMessage = "Das Budget muss zwischen 0 und 999999 Stunden liegen.")]
    public decimal? BudgetHours { get; init; }
}

/// <summary>Nummer, Filiale, Jahr und Monat bleiben nach dem Anlegen unverändert.</summary>
public record UpdateScheduleRequest
{
    public ScheduleStatus? Status { get; init; }

    [Range(0, 999999, ErrorMessage = "Das Budget muss zwischen 0 und 999999 Stunden liegen.")]
    public decimal? BudgetHours { get; init; }
}

public class ShiftFilterRequest
{
    public Guid? EmployeeId { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
}

public record CreateShiftRequest
{
    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    public Guid? EmployeeId { get; init; }

    [Required(ErrorMessage = "Dieser Wert darf nicht leer sein.")]
    public DateOnly? Date { get; init; }

    /// <summary>Beginn und Ende gelten nur zusammen; liegt das Ende vor dem Beginn, geht die Schicht über Mitternacht.</summary>
    public TimeOnly? StartTime { get; init; }
    public TimeOnly? EndTime { get; init; }

    [MaxLength(20, ErrorMessage = "Dieser Wert ist zu lang. Er darf höchstens 20 Zeichen enthalten.")]
    public string? ShiftCode { get; init; }

    public Guid? PlanningRoleId { get; init; }
    public Guid? AbsenceTypeId { get; init; }

    [MaxLength(255, ErrorMessage = "Dieser Wert ist zu lang. Er darf höchstens 255 Zeichen enthalten.")]
    public string? Remark { get; init; }
}

/// <summary>PATCH: fehlend/<c>null</c> = unverändert. Mitarbeiter und Plan ändern sich nicht (Schicht löschen und neu anlegen).</summary>
public record UpdateShiftRequest
{
    public DateOnly? Date { get; init; }
    public TimeOnly? StartTime { get; init; }
    public TimeOnly? EndTime { get; init; }

    [MaxLength(20, ErrorMessage = "Dieser Wert ist zu lang. Er darf höchstens 20 Zeichen enthalten.")]
    public string? ShiftCode { get; init; }

    public Guid? PlanningRoleId { get; init; }
    public Guid? AbsenceTypeId { get; init; }

    [MaxLength(255, ErrorMessage = "Dieser Wert ist zu lang. Er darf höchstens 255 Zeichen enthalten.")]
    public string? Remark { get; init; }
}

/// <summary>Ein einzelner Plan mit der Summe der geplanten Stunden (Schichten im Monat des Plans, Pausen abgezogen; Regeln in <c>ScheduleCalculator</c>).</summary>
public record ScheduleDetailResponse(
    Guid Id, string Number, Guid WorkplaceId, string WorkplaceCode, Guid? LocationId, int Year, int Month, ScheduleStatus Status, decimal BudgetHours, decimal PlannedHours, DateTime CreatedAt, DateTime UpdatedAt)
{
    public static ScheduleDetailResponse From(Schedule s, decimal plannedHours) =>
        new(s.Id, s.Number, s.WorkplaceId, s.Workplace!.Code, s.Workplace.LocationId, s.Year, s.Month, s.Status, s.BudgetHours, plannedHours, s.CreatedAt, s.UpdatedAt);
}

public class MatrixRequest
{
    /// <summary>Tage vor und nach dem Monat, die mitgeliefert werden (Dienstplan-Ansicht in BC: 6).</summary>
    [Range(0, 14, ErrorMessage = "Der Rand muss zwischen 0 und 14 Tagen liegen.")]
    public int MarginDays { get; init; } = 6;
}

/// <summary>Die Dienstplan-Matrix: ein Monat samt Rand, je Mitarbeiter seine Schichten und Summen.</summary>
public record MatrixResponse(
    ScheduleDetailResponse Schedule, DateOnly From, DateOnly To, IReadOnlyList<MatrixAbsenceType> AbsenceTypes, IReadOnlyList<MatrixRow> Rows);

/// <summary>Nur die Nichtverfügbarkeitsarten, auf die sich Schichten der Matrix beziehen (Kürzel für die Anzeige).</summary>
public record MatrixAbsenceType(Guid Id, string Code, string? Symbol, bool PaidTime);

/// <summary>
/// Eine Zeile der Matrix. Zuerst die Mitarbeiter der Filiale des Plans (auch ohne Schichten), danach Mitarbeiter anderer Filialen mit Schichten in diesem Plan.
/// <see cref="TargetHours"/> ist <c>null</c> ohne Vertrag bzw. bei Mitarbeitern anderer Filialen.
/// </summary>
public record MatrixRow(
    Guid EmployeeId, string EmployeeNo, string FirstName, string LastName, bool HomeEmployee,
    decimal? TargetHours, decimal PlannedHours, decimal? PlannedPercent,
    IReadOnlyList<MatrixShift> Shifts, IReadOnlyList<MatrixAway> Away);

/// <summary>Eine Schicht der Matrix; <see cref="Date"/> kann im Rand liegen (dann gehört sie zu einem Nachbarplan und zählt nicht in die Summen). <see cref="Hours"/>: die gezählten Stunden.</summary>
public record MatrixShift(
    Guid Id, DateOnly Date, TimeOnly? StartTime, TimeOnly? EndTime, string? ShiftCode, Guid? PlanningRoleId, Guid? AbsenceTypeId, string? Remark, decimal Hours);

/// <summary>Der Mitarbeiter ist an diesem Tag in einem Plan einer anderen Filiale eingeteilt (in BC als „YY &lt;Arbeitsort&gt; von - bis“ angezeigt). Zählt nicht in die Stunden dieses Plans.</summary>
public record MatrixAway(DateOnly Date, TimeOnly? StartTime, TimeOnly? EndTime, Guid WorkplaceId, string WorkplaceCode);
