using HrTool_API.Authentication;


namespace HrTool_API.Workforce;

/// <summary>
/// Schichtmuster (Export „Schichtmuster“, ~530 Muster, z. B. <c>AB/MINI1</c>). Der Export enthält nur den Kopf; die Tage/Zeiten der Muster und die Zuordnung
/// zu Mitarbeitern stehen nicht darin und sind noch nicht modelliert.
/// </summary>
[PrivilegeResource("Schichtmuster", DefinesReach = false)]
public class ShiftPattern
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    /// <summary>Code aus BC, eindeutig (Präfix = Arbeitsort bzw. Gruppe, z. B. <c>AB/VZ1</c>).</summary>
    public string Code { get; set; } = string.Empty;

    public string? Description { get; set; }

    public ShiftPatternKind Kind { get; set; }

    /// <summary>Länge des Musters in Wochen (beobachtet: 0, 1, 2, 3, 4, 6, 8; bei Tagesmustern auch 0).</summary>
    public int WeekCount { get; set; }
}
