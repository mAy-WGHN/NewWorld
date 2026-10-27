namespace Workforce.Core.Employees;

/// <summary>
/// Privileges around the employee card, registered with Access (roles, scopes and validity are set there). The wage type is stricter than
/// the rest: only HR, administrators and the management get <see cref="ReadWageType"/>. Private data may also be read by the person
/// themself (<see cref="ReadOwn"/>), who can ask HR for changes.
/// </summary>
public static class EmployeePrivileges
{
    public const string AppKey = "employee";

    public const string Read = "employee:read";
    public const string ReadPrivate = "employee:read-private";
    public const string ReadWageType = "employee:read-wage-type";
    public const string ReadOwn = "employee:read-own";
    public const string Create = "employee:create";
    public const string Update = "employee:update";
    public const string ManageLists = "employee:manage-lists";
    public const string ManageNumberSeries = "employee:manage-number-series";
    public const string RequestChangeOwn = "employee:request-change-own";
    public const string DecideChange = "employee:decide-change";

    /// <summary>The privileges with what they allow in plain words (German, shown in Access when roles are put together).</summary>
    public static readonly IReadOnlyList<(string Key, bool Scopable, string Description)> Catalog =
    [
        (Read, true, "Die Mitarbeiterkarte (Name, Personalnummer, Filiale, Funktion, Beschäftigung, Zeiterfassung) von Personen in der Reichweite ansehen, ohne private Daten und Lohnart."),
        (ReadPrivate, true, "Private Daten (Adresse, Telefon, private E-Mail, Geburtsdatum, Geschlecht) von Personen in der Reichweite ansehen (HR, Admin, Geschäftsführung)."),
        (ReadWageType, true, "Die Lohnart von Personen in der Reichweite ansehen und ändern (nur HR, Admin, Geschäftsführung)."),
        (ReadOwn, false, "Die eigene Mitarbeiterkarte einschließlich der privaten Daten ansehen (nie die Lohnart)."),
        (Create, true, "Neue Mitarbeitende anlegen (die Personalnummer wird aus dem Nummernkreis vergeben); nur für Filialen in der Reichweite."),
        (Update, true, "Die Mitarbeiterkarte von Personen in der Reichweite ändern."),
        (ManageLists, false, "Die Auswahllisten pflegen (Funktion, Rolle, Region, Arbeitsort, Arbeitsverhältnis, Beschäftigungsverhältniscode, Berechtigungsgruppe, Lohnart)."),
        (ManageNumberSeries, false, "Den Nummernkreis der Personalnummern ansehen und die zuletzt vergebene Nummer und den Bereich setzen."),
        (RequestChangeOwn, false, "Für die eigenen privaten Kontaktdaten (Adresse, Telefon, private E-Mail) eine Änderung beantragen oder etwas melden und die eigenen Anträge ansehen. Ändern kann die Person nichts selbst."),
        (DecideChange, true, "Änderungsanträge von Personen in der Reichweite ansehen, genehmigen oder ablehnen (HR). Genehmigte Änderungen werden in die Mitarbeiterkarte übernommen."),
    ];
}
