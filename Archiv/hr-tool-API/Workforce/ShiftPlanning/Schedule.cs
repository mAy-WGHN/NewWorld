using HrTool_API.Authentication;

using HrTool_API.Domain.Entities;

namespace HrTool_API.Workforce;

/// <summary>
/// Dienstplan eines Arbeitsorts für einen Monat (Export „Mitarbeiterliste“ — trotz des Namens die Liste der Dienstpläne; Periode „Month“).
/// Je Arbeitsort und Monat gibt es genau einen Plan (im Export ohne Ausnahme). Summen (Geplant, Tatsächlich, Beträge, Warnungen, Geplant %) werden aus den Schichten berechnet bzw. kommen
/// aus der Zeiterfassung und sind hier nicht gespeichert; nur das <see cref="BudgetHours"/> ist eine Vorgabe.
/// </summary>
[PrivilegeResource("Dienstpläne", ScopedActions = CrudAction.All)] // Schichten (Shift) haben kein eigenes Recht: Lesen = schedule:read, ändern = schedule:update
public class Schedule : IHasUpdatedAt
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    /// <summary>Plannummer aus BC („Nr.“, z. B. <c>DPAB0000039</c>, <c>AO0000192</c>), eindeutig; in der Arbeitsplanansicht „Aktueller Dienstplan“.</summary>
    public string Number { get; set; } = string.Empty;

    /// <summary>Arbeitsort des Plans. Die Filiale und damit der Scope folgen daraus.</summary>
    public Guid WorkplaceId { get; set; }
    public Workplace? Workplace { get; set; }

    public int Year { get; set; }

    /// <summary>1–12 (im Export als „April  2021“).</summary>
    public int Month { get; set; }

    public ScheduleStatus Status { get; set; } = ScheduleStatus.Draft;

    /// <summary>„Total Budget (Std.)“: Stundenbudget des Plans.</summary>
    public decimal BudgetHours { get; set; }

    public ICollection<Shift> Shifts { get; } = [];

    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    /// <summary>„Zuletzt geändert“.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
