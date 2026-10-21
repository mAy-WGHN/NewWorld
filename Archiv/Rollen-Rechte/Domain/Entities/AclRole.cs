using Rollen_Rechte.Authentication;

namespace Rollen_Rechte.Domain.Entities;

/// <summary>
/// Berechtigungsrolle (Muster: Shopware "acl_role"). Rechte hängen an der Rolle, Rollen an Usern
/// (<see cref="AclUserRole"/>); ein User kann mehrere Rollen haben.
/// </summary>
[PrivilegeResource("Rollen und Rechte", Key = "role", ScopedActions = CrudAction.Read, DefinesReach = false)]
[PrivilegeAction("assign", "Rollen an Benutzer vergeben", Scopable = true)]
public class AclRole
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Rechte als JSON-Liste von Strings (z. B. "report:read"); die Schlüssel legt die Berechtigungslogik fest.</summary>
    public List<string> Privileges { get; set; } = [];

    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
