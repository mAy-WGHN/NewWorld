namespace Workforce.Core.Unavailability;

/// <summary>
/// Privileges for the unavailability list. Reading uses the absence privileges (<c>absence:read-own</c>, <c>absence:read-availability</c>,
/// <c>absence:read-reason</c>): who may see the kind is the same question as for sickness and vacation.
/// </summary>
public static class UnavailabilityPrivileges
{
    public const string Create = "unavailability:create";
    public const string Delete = "unavailability:delete";
    public const string ManageKinds = "unavailability:manage-kinds";

    /// <summary>The privileges with what they allow in plain words (German, shown in Access when roles are put together).</summary>
    public static readonly IReadOnlyList<(string Key, bool Scopable, string Description)> Catalog =
    [
        (Create, true, "Nicht-Verfügbarkeiten (z. B. Sonderurlaub, Elternzeit, Fortbildung, Freier Tag) für Personen in der Reichweite eintragen (HR). Krank und Urlaub kommen aus den Abwesenheiten."),
        (Delete, true, "Von Hand eingetragene Nicht-Verfügbarkeiten von Personen in der Reichweite wieder entfernen (HR)."),
        (ManageKinds, false, "Die Arten der Nicht-Verfügbarkeit pflegen (Code, Beschreibung, bezahlte Zeit, Zeichen im Dienstplan); Arten werden nur deaktiviert, nie gelöscht."),
    ];
}
