namespace Rollen_Rechte.Domain.Entities;

/// <summary>
/// Rollenzuweisung: User ↔ <see cref="AclRole"/> (Muster: Shopware "acl_user_role"); Schlüssel ist das Paar.
/// Optional eingeschränkt auf <see cref="Scopes"/> und zeitlich begrenzt (Urlaubsvertretung, Zweitmeinung).
/// </summary>
public class AclUserRole
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public Guid AclRoleId { get; set; }
    public AclRole AclRole { get; set; } = null!;

    /// <summary>
    /// false = die Rechte der Rolle gelten uneingeschränkt. true = nur im Scope (<see cref="Scopes"/> und ggf. <see cref="HomeScope"/>) —
    /// auch bei leerer Liste, dann reicht die Zuweisung nirgends hin. So macht kein Entfernen von Mitgliedern aus einer Einschränkung eine Freigabe.
    /// </summary>
    public bool Scoped { get; set; }

    /// <summary>Nur mit <see cref="Scoped"/>: zusätzlich die Heimatfiliale des Users (<c>users.home_location_id</c>).</summary>
    public bool HomeScope { get; set; }

    public List<AclUserRoleScope> Scopes { get; set; } = [];

    /// <summary>Gültig ab (UTC); null = sofort.</summary>
    public DateTime? ValidFrom { get; set; }

    /// <summary>Gültig bis (UTC, exklusiv); null = unbefristet. Abgelaufene Zuweisungen werden ignoriert, nicht gelöscht.</summary>
    public DateTime? ValidTo { get; set; }

    public bool IsValidAt(DateTime utcNow) => (ValidFrom is null || ValidFrom <= utcNow) && (ValidTo is null || ValidTo > utcNow);

    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
