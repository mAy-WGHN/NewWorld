using HrTool_API.Authentication;

using HrTool_API.Domain.Entities;

namespace HrTool_API.Hr;

/// <summary>
/// Mitarbeiter in der Personalplanung (Mitarbeiterliste, Personalplanung). <see cref="EmployeeNo"/> ist der Schlüssel aus BC („Nr.“, fünfstellig mit führenden Nullen)
/// und zugleich die Personalnummer/der <c>username</c> im Token von Rollen-Rechte; <see cref="UserId"/> (<c>sub</c>) verbindet den Mitarbeiter mit seinem Login, falls er einen hat.
/// Stammdaten kommen aus BC; was hier fachlich dazukommt (Dienstpläne, Nicht-Verfügbarkeiten), hängt an dieser Zeile.
/// </summary>
[PrivilegeResource("Mitarbeiter", Actions = CrudAction.Read | CrudAction.Update | CrudAction.Delete, DeleteLabel = "Deaktivieren",
    ScopedActions = CrudAction.Read | CrudAction.Update | CrudAction.Delete)] // Anlegen: Import aus BC
public class Employee : IHasUpdatedAt
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    /// <summary>Personalnummer aus BC („Nr.“), eindeutig, als Text wegen der führenden Nullen.</summary>
    public string EmployeeNo { get; set; } = string.Empty;

    /// <summary>User-ID bei Rollen-Rechte (<c>sub</c>); <c>null</c>, solange der Mitarbeiter keinen Login hat. Ohne Fremdschlüssel.</summary>
    public Guid? UserId { get; set; }

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;

    /// <summary>Funktion/Stellenbezeichnung (Freitext in BC, z. B. „Aushilfe“); nicht bei allen Mitarbeitern gepflegt.</summary>
    public string? Function { get; set; }

    /// <summary>Geschäftliche E-Mail-Adresse; nur bei einem Teil der Mitarbeiter vorhanden.</summary>
    public string? Email { get; set; }

    public string? Remark { get; set; }

    public Guid? ContractId { get; set; }
    public EmploymentContract? Contract { get; set; }

    /// <summary>Arbeitsort des Mitarbeiters (Spalte „Arbeitsort“); <c>null</c> bei Mitarbeitern ohne Zuordnung (im Export 84 von 772). Die Filiale und damit der Scope folgen daraus.</summary>
    public Guid? WorkplaceId { get; set; }
    public Workplace? Workplace { get; set; }

    public Guid? PlanningRoleId { get; set; }
    public PlanningRole? PlanningRole { get; set; }

    /// <summary>Region aus BC (z. B. <c>REGION_3</c>, <c>AUGSBURG</c>, <c>FIBU</c>); nur bei Führungs-/Verwaltungskräften gepflegt. Als Code, die Regionen sind kein eigener Stamm.</summary>
    public string? RegionCode { get; set; }

    /// <summary>„Schichttausch Status: Schichtwechsel aktiviert“ der Arbeitsplanansicht: Darf der Mitarbeiter Schichten tauschen?</summary>
    public bool ShiftSwapEnabled { get; set; }

    public bool Active { get; set; } = true;

    public CashierProfile? CashierProfile { get; set; }

    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
