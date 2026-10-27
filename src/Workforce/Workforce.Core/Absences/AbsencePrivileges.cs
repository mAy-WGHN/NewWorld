namespace Workforce.Core.Absences;

/// <summary>
/// Privileges of the absence endpoints, registered with Access (the ACL decides who gets them, with which reach). Reach for
/// scopable ones comes from Access: the stores and employees of the assignment.
/// </summary>
public static class AbsencePrivileges
{
    public const string AppKey = "absence";

    // own data
    public const string ReadOwn = "absence:read-own";
    public const string CreateOwn = "absence:create-own";
    public const string UpdateOwn = "absence:update-own";

    // others (scopable: stores / employees in reach)
    /// <summary>Sees that colleagues are absent ("abwesend"), nothing more.</summary>
    public const string ReadAvailability = "absence:read-availability";

    /// <summary>
    /// Full view: period, status and type (sickness/vacation): the person themself, supervisors and HR. There is deliberately no
    /// level in between: the status of a sick note ("reported") would reveal the type.
    /// </summary>
    public const string ReadReason = "absence:read-reason";

    /// <summary>Sees the BC transfer status.</summary>
    public const string ReadSync = "absence:read-sync";

    public const string Create = "absence:create";
    public const string Approve = "absence:approve";
    public const string ManageEntitlement = "absence:manage-entitlement";

    /// <summary>
    /// Sets whether a store is open on public holidays (e.g. railway-station stores); scopable, so district managers maintain
    /// the stores of their district.
    /// </summary>
    public const string ManageHolidayOpening = "absence:manage-holiday-opening";

    /// <summary>Sets when additional vacation days (leftover, special leave) expire, for everybody (HR); not scopable.</summary>
    public const string ManageVacationRules = "absence:manage-vacation-rules";

    /// <summary>The privileges with what they allow in plain words (German, shown in Access when roles are put together).</summary>
    public static readonly IReadOnlyList<(string Key, bool Scopable, string Description)> Catalog =
    [
        (ReadOwn, false, "Eigene Abwesenheiten und das eigene Urlaubskonto ansehen."),
        (CreateOwn, false, "Eine eigene Krankmeldung erfassen oder Urlaub beantragen."),
        (UpdateOwn, false, "Einen eigenen Urlaubsantrag zurückziehen oder eine offene Krankmeldung beenden."),
        (ReadAvailability, true, "Sehen, dass Kolleginnen und Kollegen der Filiale abwesend sind – ohne Grund und ohne Art."),
        (ReadReason, true, "Zeitraum, Status und Art (Krank oder Urlaub) der Abwesenheiten von Personen in der Reichweite sehen (Vorgesetzte, HR)."),
        (ReadSync, true, "Den Übertragungsstatus der Abwesenheiten nach Business Central sehen."),
        (Create, true, "Krankmeldungen für Mitarbeitende in der Reichweite erfassen."),
        (Approve, true, "Urlaubsanträge von Personen in der Reichweite genehmigen oder ablehnen (nur, wer für das Thema zuständig ist)."),
        (ManageEntitlement, true, "Den Urlaubsanspruch pflegen: vertraglicher Jahresanspruch, Änderungen des Anspruchs im Jahr (z. B. andere Wochentage) und Zusatztage (Resturlaub, Sonderurlaub) von Personen in der Reichweite (HR)."),
        (ManageHolidayOpening, true, "Festlegen, ob Filialen in der Reichweite an gesetzlichen Feiertagen geöffnet sind (z. B. Bahnhofsfilialen); Feiertage zählen dort als Arbeitstage."),
        (ManageVacationRules, false, "Die Urlaubsregeln für alle festlegen (HR): ob und bis wann Zusatztage verfallen (Standard: nie), Wartezeit für neue Mitarbeitende (Standard: keine) und Rundung anteiliger Tage (Standard: auf halbe Tage auf)."),
    ];
}
