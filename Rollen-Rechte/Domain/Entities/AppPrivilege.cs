namespace Rollen_Rechte.Domain.Entities;

/// <summary>
/// Ein Recht, das eine <b>andere Anwendung</b> (Portal, HR-Tool, …) beim Start in diesem Dienst registriert hat (<c>PUT /api/apps/{appKey}/catalog</c>).
/// Dieser Dienst führt die Kataloge aller Anwendungen zusammen, damit man die Rechte in Rollen vergeben kann; durchgesetzt werden sie von der
/// jeweiligen Anwendung. Eigene Rechte dieses Dienstes entstehen dagegen aus den Entities (<c>PrivilegeResource</c>).
/// </summary>
public class AppPrivilege
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    /// <summary>Welche Anwendung das Recht registriert hat, z. B. "portal".</summary>
    public string AppKey { get; set; } = string.Empty;

    /// <summary>Das Recht im Format <c>bereich:aktion</c>, über alle Anwendungen eindeutig.</summary>
    public string Privilege { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public string AreaKey { get; set; } = string.Empty;
    public string AreaLabel { get; set; } = string.Empty;

    /// <summary>Darf dieses Recht auf einen Scope beschränkt vergeben werden (die Anwendung wendet ihn in ihren Abfragen an)?</summary>
    public bool Scopable { get; set; }

    /// <summary>Bestimmt der Scope bei diesem Bereich die Datenreichweite des Users (false z. B. bei reinen Verwaltungsbereichen)?</summary>
    public bool DefinesReach { get; set; } = true;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
