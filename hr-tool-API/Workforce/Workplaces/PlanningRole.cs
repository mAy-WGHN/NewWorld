namespace HrTool_API.Workforce;

/// <summary>Rolle in der Personalplanung (Spalte „Rolle“ der Mitarbeiterliste; beobachtet: <c>VK</c>, <c>VK_SA</c>, <c>BÜ</c>, <c>LG</c>). Die Dienstplan-Auswertung kennt Budgets je Rolle.</summary>
public class PlanningRole
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    /// <summary>Code aus BC, eindeutig.</summary>
    public string Code { get; set; } = string.Empty;
}
