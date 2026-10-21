using Rollen_Rechte.Authentication;

namespace Rollen_Rechte.Domain.Entities;

/// <summary>
/// Ein Benutzer des Unternehmensportals (Personalnummer als Username). Hier liegt nur, was für Anmeldung und Rechte nötig ist; Einstellungen
/// der einzelnen Anwendungen (z. B. Benachrichtigungen, Push-Abos) gehören der jeweiligen Anwendung und verweisen über die ID auf diesen User.
/// </summary>
[PrivilegeResource("Benutzer", DeleteLabel = "Deaktivieren", ScopedActions = CrudAction.All)]
public class User
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Eindeutig, Login-Name (bei Mitarbeitern die Personalnummer).</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Nur der Hash.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Gesetzt, sobald sich der User einmal erfolgreich per BC-Personalnummer + PIN angemeldet hat: das PIN,
    /// reversibel verschlüsselt (siehe PinProtector). Solange gesetzt, ist BC für diesen User führend.
    /// </summary>
    public string? BcPinEncrypted { get; set; }

    /// <summary>Heimatfiliale (optional): dient als Scope für Zuweisungen mit <c>HomeScope</c>. Nicht jeder User hat eine; sonst wird der Scope manuell vergeben.</summary>
    public Guid? HomeLocationId { get; set; }

    /// <summary>false = deaktiviert (Soft-Delete).</summary>
    public bool Active { get; set; } = true;
}
