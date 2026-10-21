namespace Rollen_Rechte.Authentication;

/// <summary>
/// Rechte haben das Format <c>ressource:aktion</c>. Die Rechte der Entities (read/create/update/delete und Zusatzaktionen) erzeugt der
/// <see cref="PrivilegeCatalog"/> aus <see cref="PrivilegeResourceAttribute"/>; hier stehen nur der Platzhalter und die Rechte
/// von Bereichen ohne Entity. Öffentliche Routen sind ausdrücklich <c>[AllowAnonymous]</c>, eigene Daten (<c>/api/me</c>) verlangen nur
/// die Anmeldung (<see cref="AuthPolicies.Authenticated"/>).
/// </summary>
public static class Privileges
{
    /// <summary>Alle Rechte. Platzhalter pro Ressource: <c>report:*</c>.</summary>
    public const string Everything = "*";


    /// <summary>Gilt <paramref name="required"/> durch ein Recht aus <paramref name="granted"/>? Treffer: exakt, <c>*</c> oder <c>ressource:*</c>.</summary>
    public static bool Allows(IEnumerable<string> granted, string required)
    {
        foreach (var privilege in granted)
        {
            if (privilege == Everything || privilege == required) return true;
            if (privilege.EndsWith(":*", StringComparison.Ordinal) &&
                required.StartsWith(privilege.AsSpan(0, privilege.Length - 1), StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
