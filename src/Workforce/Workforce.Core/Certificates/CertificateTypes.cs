namespace Workforce.Core.Certificates;

/// <summary>
/// Where the medical certificate (AU) of a sick note stands. Visible to the person and to everybody who holds the status privilege for
/// them (e.g. store managers, HR); the document itself only to the uploader and the reviewers (HR).
/// </summary>
public enum CertificateStatus
{
    /// <summary>Not applicable (not a sick note).</summary>
    Unknown = 0,

    /// <summary>No certificate uploaded yet (a sick note can be reported before the doctor's appointment).</summary>
    Missing = 1,

    /// <summary>Uploaded, HR has not checked it yet.</summary>
    Submitted = 2,

    /// <summary>HR confirmed that a valid certificate was uploaded.</summary>
    Verified = 3,

    /// <summary>HR rejected the latest upload; the person has to upload again.</summary>
    Rejected = 4,
}

/// <summary>State of one uploaded document.</summary>
public enum CertificateDocumentState
{
    Unknown = 0,
    Submitted = 1,
    Verified = 2,

    /// <summary>Rejected by HR; the file is kept until the retention period ends (like every other file).</summary>
    Rejected = 3,
}

/// <summary>Why HR rejected a document. A fixed list, no free text: the reason must not carry health data.</summary>
public enum CertificateRejectionReason
{
    Unknown = 0,
    Unreadable = 1,
    WrongPerson = 2,
    WrongPeriod = 3,
    NotACertificate = 4,
}

/// <summary>What happened to a document; recorded in the access log (who, what, when, never the content).</summary>
public enum CertificateAction
{
    Unknown = 0,
    Uploaded = 1,
    Downloaded = 2,
    Verified = 3,
    Rejected = 4,
    DeletedByUploader = 5,
    PurgedByRetention = 6,

    /// <summary>Deleted by somebody who holds the delete privilege (HR).</summary>
    DeletedByPersonnel = 7,

    /// <summary>HR changed the retention period (no document involved).</summary>
    RetentionChanged = 8,

    /// <summary>HR changed the reminder rhythm (no document involved).</summary>
    RemindersChanged = 9,
}

/// <summary>The reminder rhythm HR has set (see <see cref="CertificateOptions"/> for the meaning).</summary>
public sealed record CertificateReminderSettings(int FirstAfterReturnDays, int EveryDays, int MaxReminders, DateTimeOffset ChangedAt, Guid ChangedBy);

/// <summary>The retention period HR has set (days after the end of the sick note until the file is deleted).</summary>
public sealed record CertificateRetention(int DaysAfterEnd, DateTimeOffset ChangedAt, Guid ChangedBy);

/// <summary>An uploaded certificate. The bytes live in the store, encrypted; this is the metadata. Never carries the file name or any content.</summary>
public sealed class CertificateDocument
{
    public Guid Id { get; init; }

    /// <summary>
    /// Random name of the file in the separate file storage (a UUID, no extension, no relation to the document id or the original name).
    /// The original file name is never stored; date and person are only here in the database.
    /// </summary>
    public Guid StorageId { get; init; }

    public Guid AbsenceId { get; init; }

    /// <summary>The sick person (subject of the document).</summary>
    public Guid EmployeeId { get; init; }

    /// <summary>Who uploaded it: the person or somebody acting for them. The only non-reviewer who can open it.</summary>
    public Guid UploadedBy { get; init; }

    public DateTimeOffset UploadedAt { get; init; }

    /// <summary>Detected from the content (not the file name or the declared type).</summary>
    public string ContentType { get; init; } = "";

    public long Length { get; init; }

    /// <summary>Period the uploader entered from the certificate (first and last day of incapacity).</summary>
    public DateOnly CertifiedFrom { get; init; }

    public DateOnly CertifiedTo { get; init; }

    public CertificateDocumentState State { get; set; }

    public CertificateRejectionReason? RejectionReason { get; set; }

    public Guid? ReviewedBy { get; set; }

    public DateTimeOffset? ReviewedAt { get; set; }

    /// <summary>Set when the content was removed by the retention rule; the metadata stays for the status.</summary>
    public DateTimeOffset? ContentDeletedAt { get; set; }

    public bool HasContent => ContentDeletedAt is null;
}

/// <summary>An entry of the access log of the certificates.</summary>
public sealed class CertificateAccessEntry
{
    public Guid Id { get; init; }

    public DateTimeOffset At { get; init; }

    /// <summary>Who did it; <c>null</c> for the retention job.</summary>
    public Guid? ActorId { get; init; }

    public CertificateAction Action { get; init; }

    public Guid AbsenceId { get; init; }

    public Guid DocumentId { get; init; }

    /// <summary>The sick person, so entries can be found and erased per person.</summary>
    public Guid EmployeeId { get; init; }
}
