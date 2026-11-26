namespace Portal.Core;

/// <summary>
/// Privileges of the Portal API, registered with Access (decision 13). Creating a report is public (colleagues without a login report problems), so it
/// has no privilege. Reports and statistics are scopable by store ("one store is enough": a report that touches several stores is seen by everybody
/// who reaches one of them); the FAQ is for everybody to read, only its administration needs privileges.
/// </summary>
public static class PortalPrivileges
{
    public const string AppKey = "portal";

    public const string FaqRead = "faq:read";
    public const string FaqCreate = "faq:create";
    public const string FaqUpdate = "faq:update";
    public const string FaqDelete = "faq:delete";

    public const string ReportRead = "report:read";
    public const string ReportUpdate = "report:update";
    public const string ReportDelete = "report:delete";

    /// <summary>Who is told about new reports of which category (maintained by the administration; not scopable).</summary>
    public const string ReportManageRecipients = "report:manage-recipients";

    public const string StatisticRead = "statistic:read";

    /// <summary>Berichtsheft (decision 49): the weekly report of the store manager. Where regional and district leaders look comes from the hierarchy of the Berichtsheft.</summary>
    public const string LogbookWrite = "logbook:write";

    public const string LogbookReview = "logbook:review";
    public const string LogbookManageTemplate = "logbook:manage-template";
    public const string LogbookManageStructure = "logbook:manage-structure";
    public const string LogbookManageReminders = "logbook:manage-reminders";

    /// <summary>Kassenregister (Filialbetrieb): the seals of the cash registers of the stores. Scopable by store.</summary>
    public const string CashRegisterRead = "cashregister:read";

    public const string CashRegisterRecord = "cashregister:record";
    public const string CashRegisterDelete = "cashregister:delete";
    public const string CashRegisterManageReminders = "cashregister:manage-reminders";

    public const string AuditRead = "portal-audit:read";
    public const string AuditReadValues = "portal-audit:read-values";

    /// <summary>The privileges with what they allow in plain words (German, shown in Access when roles are put together).</summary>
    public static readonly IReadOnlyList<(string Key, bool Scopable, string Description)> Catalog =
    [
        (FaqRead, false, "Archivierte FAQ-Einträge und alle hochgeladenen FAQ-Bilder ansehen (die aktiven Einträge sieht jeder)."),
        (FaqCreate, false, "FAQ-Einträge anlegen und Bilder für FAQ-Antworten hochladen."),
        (FaqUpdate, false, "FAQ-Einträge ändern, archivieren und ihre Reihenfolge festlegen."),
        (FaqDelete, false, "FAQ-Einträge und nicht mehr verwendete FAQ-Bilder endgültig löschen."),
        (ReportRead, true, "Meldungen (Probleme, Diebstahl, Vorschläge …) ansehen, die mindestens eine der Filialen betreffen, für die das Recht gilt, einschließlich der archivierten."),
        (ReportUpdate, true, "Meldungen bearbeiten: Status, Kommentar, Zuordnung zu Filialen und zu einer übergeordneten Meldung. Nur für Meldungen aus dem eigenen Bereich."),
        (ReportDelete, true, "Meldungen archivieren (ausblenden) und die Verknüpfung zur übergeordneten Meldung lösen. Nur für Meldungen aus dem eigenen Bereich."),
        (ReportManageRecipients, false, "Festlegen, wer bei neuen Meldungen welcher Kategorie benachrichtigt wird (z. B. Revisor bei Diebstahl, IT bei Fehlern und Viren)."),
        (StatisticRead, true, "Statistiken zu Meldungen ansehen (je Monat, Kategorie, Filiale, Personalnummer), begrenzt auf die Filialen, für die das Recht gilt."),
        (LogbookWrite, true, "Den Wochenbericht (Berichtsheft) der eigenen Filiale ausfüllen, abgeben und nachbereiten (Filialleitung). Gilt für die Filialen, für die das Recht vergeben ist."),
        (LogbookReview, false, "Wochenberichte der eigenen Region oder des eigenen Bezirks lesen, beantworten, intern kommentieren, Stand setzen, Filialleitungen erinnern und an die Bezirksleitung eskalieren (Regional- und Bezirksleitung). Welche Filialen, ergibt sich aus der Hierarchie im Berichtsheft."),
        (LogbookManageTemplate, false, "Die Vorlage des Wochenberichts pflegen: Abschnitte mit Einzelauswahl, Mehrfachauswahl oder Freitext (Bezirksleitung)."),
        (LogbookManageStructure, false, "Die Hierarchie des Berichtsheftes pflegen: Bezirke, Regionen, Regional- und Bezirksleitungen und die Zuordnung der Filialen."),
        (LogbookManageReminders, false, "Die automatische Erinnerung an den Wochenbericht pflegen: ein oder aus, Fälligkeit, Zeitpunkte und Höchstzahl (der Text der Erinnerung läuft über die Textpflege der Personalabteilung)."),
        (CashRegisterRead, true, "Das Kassenregister ansehen: die Kassen der Filialen mit Versiegelung, letzter Erfassung und die Filialhistorie, begrenzt auf die Filialen, für die das Recht gilt."),
        (CashRegisterRecord, true, "Kassen erfassen, bearbeiten, das Siegel erneuern und das Siegel aufheben (die Kasse als nicht versiegelt vermerken), begrenzt auf die Filialen, für die das Recht gilt."),
        (CashRegisterDelete, true, "Kassen aus dem Kassenregister löschen, begrenzt auf die Filialen, für die das Recht gilt (die Filialhistorie behält den Eintrag)."),
        (CashRegisterManageReminders, false, "Die tägliche Erinnerung an nicht versiegelte Kassen pflegen: ein oder aus und die Uhrzeit (Bezirksleitungen werden immer erinnert; der Text läuft über die Textpflege der Personalabteilung)."),
        (AuditRead, false, "Das Änderungsprotokoll des Portals ansehen: wann wer (Name, Personalnummer) was angelegt, geändert oder gelöscht hat."),
        (AuditReadValues, false, "Im Änderungsprotokoll des Portals zusätzlich die geänderten Werte ansehen (nur zusammen mit dem Recht, das Protokoll zu lesen)."),
    ];

    public static readonly IReadOnlyList<string> All = [.. Catalog.Select(c => c.Key)];
}
