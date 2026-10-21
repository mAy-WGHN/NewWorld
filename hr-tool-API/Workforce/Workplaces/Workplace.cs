using HrTool_API.Authentication;

namespace HrTool_API.Workforce;

/// <summary>
/// Arbeitsort aus BC (Stamm „Arbeitsorte“, 42 Stück: <c>AB</c>, <c>SAMUC</c>, <c>HO</c> …). Mitarbeiter und Dienstpläne hängen am Arbeitsort. Fast jeder Arbeitsort ist eine Filiale
/// (<see cref="StoreCode"/>); die Filialen selbst gehören Rollen-Rechte, <see cref="LocationId"/> verbindet beide und ist die Grundlage des Scopes (ohne Fremdschlüssel).
/// Ohne Filiale bleiben <c>BS</c> (Berufsschule) und <c>SABA</c> (Samurai Bar): Ihre Daten sieht nur, wer uneingeschränkt darf.
/// </summary>
[PrivilegeResource("Arbeitsorte", Actions = CrudAction.Read | CrudAction.Update, DefinesReach = false)] // Stammdaten: lesen und die Filiale zuordnen; angelegt werden sie aus dem Stamm
public class Workplace
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    /// <summary>Kürzel aus BC („Code“), eindeutig.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>„Beschreibung“, z. B. „Augsburg Hauptbahnhof“.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>„Store Dim Code“: die Store-Nr. der Filiale (<c>0012</c>; <c>HO</c> hat <c>95</c>); <c>null</c> bei Arbeitsorten ohne Filiale.</summary>
    public string? StoreCode { get; set; }

    /// <summary>„Kostenstellen Code“ (<c>12</c>, <c>76</c> …).</summary>
    public string? CostCenterCode { get; set; }

    /// <summary>„Region“ des Arbeitsorts (<c>REGION_1</c> … <c>REGION_5</c>); bei 15 Arbeitsorten nicht gepflegt.</summary>
    public string? RegionCode { get; set; }

    /// <summary>„Aktiv für Zeitkonten“.</summary>
    public bool TimeAccountActive { get; set; }

    /// <summary>„Urlaubs-Schwellwert“ (im Stamm 1, nur für <c>HO</c> 4). Die Bedeutung ist nicht dokumentiert; vermutlich, wie viele Mitarbeiter gleichzeitig Urlaub haben dürfen.</summary>
    public int VacationThreshold { get; set; }

    /// <summary>
    /// Filiale bei Rollen-Rechte, aufgelöst über <see cref="StoreCode"/> (<c>Services/ReferenceDataMaintenance</c>); <c>null</c>, solange sie nicht aufgelöst ist oder es keine gibt.
    /// Eindeutig: je Filiale ein Arbeitsort.
    /// </summary>
    public Guid? LocationId { get; set; }
}
