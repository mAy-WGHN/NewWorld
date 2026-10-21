using HrTool_API.Domain.Entities;

namespace HrTool_API.Workforce;

/// <summary>
/// Eine Zelle des Dienstplans: was ein Mitarbeiter an einem Tag arbeitet (Dienstplan-Matrix „Short Code“ und Arbeitsplanansicht „Role &amp; Shift“).
/// Beobachtete Zelleninhalte: Zeiten (<c>9-19</c>, <c>14-1715</c>, <c>9-1430</c>), Schichtcodes (<c>VKA1</c>), Nicht-Verfügbarkeiten mit Kürzel/Zeiten (<c>URLAUB 14-20</c>,
/// <c>KRANK 14-20</c>, <c>U</c>) und Freitext (<c>Umzug nach …</c>). Die Matrix wird beim Import in diese Felder zerlegt; was sich nicht zerlegen lässt, steht in <see cref="Remark"/>.
/// Stunden ergeben sich aus den Zeiten und werden nicht gespeichert (Pausenregeln sind noch offen).
/// </summary>
public class Shift : IHasUpdatedAt
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public Guid ScheduleId { get; set; }
    public Schedule? Schedule { get; set; }

    public Guid EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public DateOnly Date { get; set; }

    /// <summary>Beginn/Ende der Schicht; <c>null</c>, wenn die Zelle nur einen Schichtcode oder ein Kürzel enthält.</summary>
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }

    /// <summary>Schichtcode der Arbeitsplanansicht (z. B. <c>VKA1</c>). Eine Schichtdefinition (Zeiten, Rolle) ist im Export nicht enthalten, deshalb nur der Code.</summary>
    public string? ShiftCode { get; set; }

    public Guid? PlanningRoleId { get; set; }
    public PlanningRole? PlanningRole { get; set; }

    /// <summary>Gesetzt, wenn der Tag mit einer Nicht-Verfügbarkeit belegt ist (<c>URLAUB 14-20</c>, <c>KRANK</c>).</summary>
    public Guid? AbsenceTypeId { get; set; }
    public AbsenceType? AbsenceType { get; set; }

    public string? Remark { get; set; }

    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
