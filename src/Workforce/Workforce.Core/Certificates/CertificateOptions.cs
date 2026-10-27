namespace Workforce.Core.Certificates;

/// <summary>
/// Settings of the certificate upload (section <c>Workforce:Certificates</c>). Retention and reminder values are defaults that HR can
/// overwrite at runtime; the data protection officer still has to confirm them (open decision).
/// </summary>
public sealed class CertificateOptions
{
    /// <summary>Largest file in bytes.</summary>
    public int MaxFileBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>Documents per sick note (several pages or follow-up certificates).</summary>
    public int MaxDocumentsPerAbsence { get; set; } = 5;

    /// <summary>A certificate may start this many days before the reported start (doctors back-date).</summary>
    public int BackdatingDays { get; set; } = 3;

    /// <summary>
    /// Default: the first reminder comes this many days after the first day back (the day after the sick note ends); 0 = on that day.
    /// Never earlier, and never while the sick note has no end. HR can set their own value at runtime (privilege
    /// <c>certificate:manage-retention</c>); that value wins over this default.
    /// </summary>
    public int FirstReminderAfterReturnDays { get; set; }

    /// <summary>Default: further reminders at this interval (days, at least 1). HR can change it.</summary>
    public int ReminderEveryDays { get; set; } = 2;

    /// <summary>Default: no more reminders after this many. HR can change it.</summary>
    public int MaxReminders { get; set; } = 5;

    /// <summary>The upper limit HR may set for each reminder value (days / count).</summary>
    public int MaxReminderSettingDays { get; set; } = 90;

    /// <summary>
    /// Default: the file is deleted this many days after the last day of the sick note (metadata stays). 0 = delete at the end day.
    /// HR can set their own value at runtime (privilege <c>certificate:manage-retention</c>); that value wins over this default.
    /// </summary>
    public int RetentionAfterEndDays { get; set; } = 365;

    /// <summary>The longest retention period HR may set.</summary>
    public int MaxRetentionAfterEndDays { get; set; } = 3650;

    /// <summary>Files in the storage without a document are only deleted when they are older than this (uploads in progress).</summary>
    public int OrphanGraceMinutes { get; set; } = 60;

    /// <summary>Entries of the access log are deleted after this many days.</summary>
    public int AccessLogRetentionDays { get; set; } = 365;
}
