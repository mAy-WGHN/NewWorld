using HrTool_API.Authentication;

using HrTool_API.Domain.Entities;

namespace HrTool_API.Workforce;

/// <summary>
/// Nicht-Verfügbarkeit eines Mitarbeiters (Seite „Employee Non Availability“, Export „Mitarbeiter Nicht-Verfügbarkeit“: Urlaub, Krankheit, Freistellung …).
/// Eine Genehmigung gibt es nur bei **Urlaubsanträgen** (<see cref="AbsenceType.RequiresApproval"/>): Der Mitarbeiter beantragt, der Vorgesetzte akzeptiert, danach entscheidet HR im „HR Urlaubscockpit“.
/// Alle anderen Arten pflegt nur HR im Dienstplan ein und sie gelten sofort als genehmigt (<see cref="ApprovalStatus"/> bleibt leer).
/// </summary>
[PrivilegeResource("Abwesenheiten", ScopedActions = CrudAction.All)]
public class Absence : IHasUpdatedAt
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public Guid EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    /// <summary>„Art“.</summary>
    public Guid AbsenceTypeId { get; set; }
    public AbsenceType? AbsenceType { get; set; }

    public DateOnly DateFrom { get; set; }
    public DateOnly DateTo { get; set; }

    /// <summary>„Tage“ (im Cockpit „Arbeitstage (berechnet)“): von BC berechnet — Samstag und Sonntag zählen mit, Feiertage und freie Tage nicht, vermutlich nach Schichtmuster. Wird übernommen, nicht selbst gerechnet.</summary>
    public decimal Days { get; set; }

    /// <summary>„Zeit von/bis“: nur bei stundenweiser Nicht-Verfügbarkeit.</summary>
    public TimeOnly? TimeFrom { get; set; }
    public TimeOnly? TimeTo { get; set; }

    public string? Remark { get; set; }

    /// <summary>
    /// „Genehmigungsstatus“ (Text aus BC), nur bei Urlaubsanträgen. Belegt ist nur <c>Vorgesetzter akzeptiert</c> (Antrag wartet auf HR; so zeigt ihn das Urlaubscockpit ohne „Alle Status anzeigen“).
    /// Weitere Werte (genehmigt, abgelehnt …) sind nicht belegt; deshalb Text statt Enum. <c>null</c> = keine Genehmigung nötig.
    /// </summary>
    public string? ApprovalStatus { get; set; }

    /// <summary>„Ablehnungsgrund“, wenn HR den Antrag ablehnt.</summary>
    public string? RejectionReason { get; set; }

    /// <summary>„Urlaubstage“: davon Urlaub, der vom Urlaubskonto abgeht. Meist gleich <see cref="Days"/> (28 von 29 Anträgen im Cockpit); 0, wenn der Antrag nichts vom Konto abzieht.</summary>
    public decimal VacationDays { get; set; }

    /// <summary>„Beantragt am“.</summary>
    public DateTime? RequestedAt { get; set; }

    /// <summary>„Bestätigt“.</summary>
    public bool Confirmed { get; set; }

    /// <summary>„Erfasst von“: Benutzername in BC (Text, keine User-ID).</summary>
    public string? RecordedBy { get; set; }

    /// <summary>„Erfassungsdatum“.</summary>
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
