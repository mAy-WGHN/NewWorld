namespace HrTool_API.Hr;

/// <summary>
/// Kassendaten eines Mitarbeiters (Kassiererübersicht; die ID dort ist die Personalnummer). 1:1 zu <see cref="Employee"/>, nur für Mitarbeiter mit Kassenzugang.
/// Die Profil-Felder (Schnittstelle, Menü, POS-Stil) sind im Export durchgehend leer und bleiben optional.
/// </summary>
public class CashierProfile
{
    /// <summary>Gleichzeitig Primär- und Fremdschlüssel.</summary>
    public Guid EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    /// <summary>„Name auf Bon“; im Export teils der Nachname, teils ein Platzhalter.</summary>
    public string? ReceiptName { get; set; }

    /// <summary>„Arbeitsverhältnis“: <c>Kassierer</c> oder <c>Verkäufer</c> (Text aus BC, nicht mit dem <see cref="EmploymentContract"/> zu verwechseln).</summary>
    public string? WorkRelation { get; set; }

    /// <summary>„Filialnr.“ (Store-Nr. wie bei Rollen-Rechte, vierstellig; <c>HO</c> für die Verwaltung). Kann vom Arbeitsort des Mitarbeiters abweichen.</summary>
    public string? StoreCode { get; set; }

    /// <summary>„Berechtigungsgruppe“ der Kasse: <c>KASSIERER</c> oder <c>MANAGER</c>.</summary>
    public string? PermissionGroup { get; set; }

    /// <summary>„Manager Benutzerrechte“ (im Export <c>Nein</c> oder leer).</summary>
    public bool ManagerRights { get; set; }

    public string? InterfaceProfile { get; set; }
    public string? MenuProfile { get; set; }
    public string? PosStyleProfile { get; set; }
}
