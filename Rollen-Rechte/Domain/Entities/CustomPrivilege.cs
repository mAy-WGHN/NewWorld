using Rollen_Rechte.Authentication;

namespace Rollen_Rechte.Domain.Entities;

/// <summary>
/// Zur Laufzeit angelegtes Recht (<c>bereich:aktion</c>, z. B. "report:export"), das neben den aus Entities generierten Rechten
/// (<see cref="PrivilegeResourceAttribute"/>) in Rollen vergeben werden kann. Die API prüft nur, dass der Schlüssel existiert, und
/// berücksichtigt ihn beim Auflösen (<c>/api/me/privileges</c>, Platzhalter). Eine Route erzwingt ihn per <c>[HasCustomPrivilege("…")]</c>;
/// ohne solche Prüfung steuert er nur, was das Frontend ein- und ausblendet.
/// </summary>
[PrivilegeResource("Eigene Rechte", Key = "custom_privilege")]
public class CustomPrivilege
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    /// <summary>Unveränderlicher Schlüssel im Format <c>bereich:aktion</c> (Kleinbuchstaben, Ziffern, Unterstrich).</summary>
    public string Key { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
