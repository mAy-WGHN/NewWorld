namespace Workforce.Core.Certificates;

/// <summary>
/// Privileges around the medical certificate (AU) of a sick note. Each one is granted on its own in Access (roles, scopes, validity);
/// nothing is granted by default except what a role gets by configuration. The document is stricter than everything else in the service:
/// only the uploader and holders of <see cref="Review"/> can open it. Supervisors, colleagues and even the sick person (if somebody else
/// uploaded) cannot, unless they hold <see cref="Review"/>.
/// </summary>
public static class CertificatePrivileges
{
    // uploading
    public const string UploadOwn = "certificate:upload-own";

    /// <summary>Upload for another person (scopable), e.g. a store manager who receives the certificate.</summary>
    public const string UploadFor = "certificate:upload-for";

    // status (no content)
    /// <summary>Sees whether a certificate is missing, submitted, verified or rejected (scopable), also as a filterable overview.</summary>
    public const string ReadStatus = "certificate:read-status";

    // the document
    /// <summary>Opens the documents of persons in reach and confirms or rejects them (HR).</summary>
    public const string Review = "certificate:review";

    /// <summary>Deletes documents of persons in reach, in any state (HR). The uploader can always withdraw an unchecked upload.</summary>
    public const string Delete = "certificate:delete";

    // settings
    /// <summary>Sets how long after the end of a sick note the files are kept until they are deleted automatically (HR).</summary>
    public const string ManageRetention = "certificate:manage-retention";

    // oversight
    /// <summary>Reads the access log of the certificates (who opened or decided what, when). Never the content.</summary>
    public const string ReadAccessLog = "certificate:read-access-log";

    /// <summary>The privileges with what they allow in plain words (German, shown in Access when roles are put together).</summary>
    public static readonly IReadOnlyList<(string Key, bool Scopable, string Description)> Catalog =
    [
        (UploadOwn, false, "Die eigene Arbeitsunfähigkeitsbescheinigung (AU) hochladen."),
        (UploadFor, true, "Eine AU für Personen in der Reichweite hochladen, z. B. wenn sie in der Filiale abgegeben wird."),
        (ReadStatus, true, "Sehen, ob eine AU fehlt, eingereicht, geprüft oder abgelehnt ist (nie den Inhalt), auch als gefilterte Übersicht."),
        (Review, true, "AU-Dateien von Personen in der Reichweite öffnen und bestätigen oder mit Grund ablehnen (HR)."),
        (Delete, true, "AU-Dateien in jedem Zustand löschen (HR). Die hochladende Person kann ungeprüfte Uploads selbst zurücknehmen."),
        (ManageRetention, false, "Löschfrist der AU-Dateien nach Ende der Krankmeldung und den Erinnerungsrhythmus einstellen (HR)."),
        (ReadAccessLog, false, "Das Zugriffsprotokoll der AUs lesen: wer wann hochgeladen, geöffnet oder entschieden hat, nie den Inhalt."),
    ];
}
