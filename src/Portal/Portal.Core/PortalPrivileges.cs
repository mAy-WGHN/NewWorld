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
        (AuditRead, false, "Das Änderungsprotokoll des Portals ansehen: wann wer (Name, Personalnummer) was angelegt, geändert oder gelöscht hat."),
        (AuditReadValues, false, "Im Änderungsprotokoll des Portals zusätzlich die geänderten Werte ansehen (nur zusammen mit dem Recht, das Protokoll zu lesen)."),
    ];

    public static readonly IReadOnlyList<string> All = [.. Catalog.Select(c => c.Key)];
}
