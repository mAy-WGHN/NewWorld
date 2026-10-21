namespace Portal.Authentication;

/// <summary>
/// Rechte haben das Format <c>ressource:aktion</c>. Die Rechte der Entities (read/create/update/delete und Zusatzaktionen) erzeugt der
/// <see cref="PrivilegeCatalog"/> aus <see cref="PrivilegeResourceAttribute"/>; hier stehen nur die Rechte von Bereichen ohne Entity.
/// Öffentliche Routen sind ausdrücklich <c>[AllowAnonymous]</c>. Vergeben und aufgelöst werden Rechte von Rollen-Rechte.
/// </summary>
public static class Privileges
{
    public const string StatisticRead = "statistic:read";
}
