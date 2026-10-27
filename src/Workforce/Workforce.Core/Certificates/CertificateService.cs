using Notifications.Contracts;
using Workforce.Contracts;
using Workforce.Core.Absences;

namespace Workforce.Core.Certificates;

/// <summary>Result of a certificate use case. Carries no content of any file and no personal data.</summary>
public sealed record CertificateResult(AbsenceError Error, CertificateDocument? Document = null, Absence? Absence = null)
{
    public static CertificateResult Ok(CertificateDocument document, Absence absence) => new(AbsenceError.None, document, absence);

    public static CertificateResult Fail(AbsenceError error) => new(error);
}

public sealed record CertificateContent(AbsenceError Error, byte[]? Bytes = null, string? ContentType = null)
{
    public static CertificateContent Fail(AbsenceError error) => new(error);
}

/// <summary>
/// The medical certificate (AU) of a sick note: upload (also afterwards), the review by HR, reminders and deletion. Free of infrastructure.
/// Callers check the privileges (who may upload for whom, who may review) and save the unit of work afterwards.
/// What the service itself enforces: the document is only readable by its uploader or by a reviewer, every use is logged without content,
/// the period entered with the upload keeps the absence up to date, and the file is deleted by the retention period (set by HR) or by hand.
/// </summary>
public sealed class CertificateService(
    IAbsenceStore absences,
    ICertificateStore certificates,
    IPeopleDirectory people,
    IWorkforceMessages messages,
    AbsenceService absenceService,
    IMalwareScanner scanner,
    TimeProvider time,
    WorkforceOptions options)
{
    public const string ReminderTemplate = "workforce.sick-note-certificate-reminder.employee";
    public const string RejectedTemplate = "workforce.sick-note-certificate-rejected.employee";

    private static readonly NotificationChannel[] Channels = [NotificationChannel.InApp, NotificationChannel.Push];

    // ------------------------------------------------------------------ upload

    /// <summary>
    /// Adds a certificate to a sick note. <paramref name="certifiedFrom"/>/<paramref name="certifiedTo"/> are the first and last day of
    /// incapacity as the uploader read them from the certificate; they extend the sick note (an open one gets its end), within the
    /// back-dating limit. The type is recognised from the content, the file name is never stored.
    /// </summary>
    public async Task<CertificateResult> UploadAsync(
        Guid absenceId, Guid uploaderId, ReadOnlyMemory<byte> content, DateOnly certifiedFrom, DateOnly certifiedTo, CancellationToken cancellationToken)
    {
        var limits = options.Certificates;
        if (await absences.GetAsync(absenceId, cancellationToken) is not { } absence)
        {
            return CertificateResult.Fail(AbsenceError.NotFound);
        }

        if (absence is not { Type: AbsenceType.Sickness, Status: AbsenceStatus.Reported })
        {
            return CertificateResult.Fail(AbsenceError.InvalidState);
        }

        if (certifiedTo < certifiedFrom || certifiedTo.DayNumber - certifiedFrom.DayNumber > 366)
        {
            return CertificateResult.Fail(AbsenceError.InvalidPeriod);
        }

        // The certificate has to belong to this sick note: it may start a few days earlier and must continue or overlap the reported period.
        if (certifiedFrom < absence.From.AddDays(-limits.BackdatingDays)
            || certifiedTo < absence.From
            || (absence.To is { } reportedEnd && certifiedFrom > reportedEnd.AddDays(1)))
        {
            return CertificateResult.Fail(AbsenceError.InvalidPeriod);
        }

        if (content.IsEmpty || content.Length > limits.MaxFileBytes)
        {
            return CertificateResult.Fail(AbsenceError.FileTooLarge);
        }

        if (FileSignature.Detect(content.Span) is not { } contentType)
        {
            return CertificateResult.Fail(AbsenceError.UnsupportedFileType);
        }

        await absences.LockEmployeeAsync(absence.EmployeeId, cancellationToken);
        var existing = await certificates.ForAbsenceAsync(absenceId, cancellationToken);
        if (existing.Count(d => d.State != CertificateDocumentState.Rejected) >= limits.MaxDocumentsPerAbsence)
        {
            return CertificateResult.Fail(AbsenceError.TooManyDocuments);
        }

        if (!await scanner.IsCleanAsync(content, cancellationToken))
        {
            return CertificateResult.Fail(AbsenceError.FileRejected);
        }

        var newFrom = certifiedFrom < absence.From ? certifiedFrom : absence.From;
        DateOnly? newTo = absence.To is { } end ? (certifiedTo > end ? certifiedTo : end) : certifiedTo;
        if (newFrom != absence.From || newTo != absence.To)
        {
            var error = await absenceService.ChangeSickNotePeriodAsync(absence, newFrom, newTo, cancellationToken);
            if (error != AbsenceError.None)
            {
                return CertificateResult.Fail(error);
            }
        }

        var now = time.GetUtcNow();
        var document = new CertificateDocument
        {
            Id = Guid.CreateVersion7(now),
            StorageId = Guid.NewGuid(),
            AbsenceId = absenceId,
            EmployeeId = absence.EmployeeId,
            UploadedBy = uploaderId,
            UploadedAt = now,
            ContentType = contentType,
            Length = content.Length,
            CertifiedFrom = certifiedFrom,
            CertifiedTo = certifiedTo,
            State = CertificateDocumentState.Submitted,
        };
        await certificates.AddAsync(document, content.ToArray(), cancellationToken);
        absence.CertifiedThrough = absence.CertifiedThrough is { } through && through > certifiedTo ? through : certifiedTo;
        absence.CertificateStatus = Derive([.. existing, document]);
        Log(document, CertificateAction.Uploaded, uploaderId, now);
        return CertificateResult.Ok(document, absence);
    }

    // ------------------------------------------------------------------ review (HR)

    /// <summary>HR confirms that a valid certificate was uploaded. Nobody reviews a document about themselves.</summary>
    public async Task<CertificateResult> VerifyAsync(Guid documentId, Guid reviewerId, CancellationToken cancellationToken)
    {
        if (await certificates.GetAsync(documentId, cancellationToken) is not { } document)
        {
            return CertificateResult.Fail(AbsenceError.NotFound);
        }

        if (document.EmployeeId == reviewerId)
        {
            return CertificateResult.Fail(AbsenceError.NotAllowed);
        }

        if (document.State != CertificateDocumentState.Submitted || await absences.GetAsync(document.AbsenceId, cancellationToken) is not { } absence)
        {
            return CertificateResult.Fail(AbsenceError.InvalidState);
        }

        var now = time.GetUtcNow();
        document.State = CertificateDocumentState.Verified;
        document.ReviewedBy = reviewerId;
        document.ReviewedAt = now;
        absence.CertificateStatus = Derive(await certificates.ForAbsenceAsync(document.AbsenceId, cancellationToken));
        Log(document, CertificateAction.Verified, reviewerId, now);
        return CertificateResult.Ok(document, absence);
    }

    /// <summary>
    /// HR rejects a document (fixed reasons, no free text). The file is kept (HR and the uploader can still open it) and is deleted
    /// by the retention rule like any other; the person is asked to upload again.
    /// </summary>
    public async Task<CertificateResult> RejectAsync(Guid documentId, Guid reviewerId, CertificateRejectionReason reason, CancellationToken cancellationToken)
    {
        if (reason == CertificateRejectionReason.Unknown || !Enum.IsDefined(reason))
        {
            return CertificateResult.Fail(AbsenceError.InvalidState);
        }

        if (await certificates.GetAsync(documentId, cancellationToken) is not { } document)
        {
            return CertificateResult.Fail(AbsenceError.NotFound);
        }

        if (document.EmployeeId == reviewerId)
        {
            return CertificateResult.Fail(AbsenceError.NotAllowed);
        }

        if (document.State != CertificateDocumentState.Submitted || await absences.GetAsync(document.AbsenceId, cancellationToken) is not { } absence)
        {
            return CertificateResult.Fail(AbsenceError.InvalidState);
        }

        var now = time.GetUtcNow();
        document.State = CertificateDocumentState.Rejected;
        document.RejectionReason = reason;
        document.ReviewedBy = reviewerId;
        document.ReviewedAt = now;
        absence.CertificateStatus = Derive(await certificates.ForAbsenceAsync(document.AbsenceId, cancellationToken));
        Log(document, CertificateAction.Rejected, reviewerId, now);

        var recipients = new[] { document.EmployeeId, document.UploadedBy }.Distinct().ToList();
        Notify(RejectedTemplate, recipients, absence, $"{document.Id}:rejected", now);
        return CertificateResult.Ok(document, absence);
    }

    // ------------------------------------------------------------------ deleting

    /// <summary>
    /// Deletes a document by hand. The uploader can withdraw an upload that has not been checked yet. Somebody who holds the delete
    /// privilege for the person (<paramref name="actorMayDelete"/>, HR) can delete in any state, but not a document about themselves
    /// that somebody else uploaded. A checked (verified) certificate loses its file only: the metadata and the status stay, so nobody is
    /// reminded again, exactly as with the retention rule. Every other document is removed completely and the status is derived anew.
    /// </summary>
    public async Task<CertificateResult> DeleteAsync(Guid documentId, Guid actorId, bool actorMayDelete, CancellationToken cancellationToken)
    {
        if (await certificates.GetAsync(documentId, cancellationToken) is not { } document || (document.UploadedBy != actorId && !actorMayDelete))
        {
            return CertificateResult.Fail(AbsenceError.NotFound);
        }

        if (actorMayDelete && document.EmployeeId == actorId && document.UploadedBy != actorId)
        {
            return CertificateResult.Fail(AbsenceError.NotAllowed);
        }

        if (await absences.GetAsync(document.AbsenceId, cancellationToken) is not { } absence
            || (!actorMayDelete && document.State != CertificateDocumentState.Submitted))
        {
            return CertificateResult.Fail(AbsenceError.InvalidState);
        }

        var now = time.GetUtcNow();
        if (document.State == CertificateDocumentState.Verified)
        {
            if (!document.HasContent)
            {
                return CertificateResult.Fail(AbsenceError.InvalidState);
            }

            await certificates.RemoveContentAsync(document, cancellationToken);
            document.ContentDeletedAt = now;
        }
        else
        {
            await certificates.RemoveAsync(document, cancellationToken);
            absence.CertificateStatus = Derive((await certificates.ForAbsenceAsync(document.AbsenceId, cancellationToken)).Where(d => d.Id != document.Id));
        }

        Log(document, actorMayDelete ? CertificateAction.DeletedByPersonnel : CertificateAction.DeletedByUploader, actorId, now);
        return CertificateResult.Ok(document, absence);
    }

    // ------------------------------------------------------------------ retention (set by HR)

    /// <summary>The retention period in force: the value HR has set, otherwise the configured default.</summary>
    public async Task<(int Days, bool IsDefault, DateTimeOffset? ChangedAt, Guid? ChangedBy)> GetRetentionAsync(CancellationToken cancellationToken) =>
        await certificates.GetRetentionAsync(cancellationToken) is { } set
            ? (set.DaysAfterEnd, false, set.ChangedAt, set.ChangedBy)
            : (options.Certificates.RetentionAfterEndDays, true, null, null);

    /// <summary>HR sets after how many days past the end of a sick note the files are deleted automatically (0 up to the configured maximum).</summary>
    public async Task<AbsenceError> SetRetentionAsync(int daysAfterEnd, Guid actorId, CancellationToken cancellationToken)
    {
        if (daysAfterEnd < 0 || daysAfterEnd > options.Certificates.MaxRetentionAfterEndDays)
        {
            return AbsenceError.InvalidRetention;
        }

        var now = time.GetUtcNow();
        await certificates.SetRetentionAsync(new CertificateRetention(daysAfterEnd, now, actorId), cancellationToken);
        certificates.Log(new CertificateAccessEntry
        {
            Id = Guid.CreateVersion7(now),
            At = now,
            ActorId = actorId,
            Action = CertificateAction.RetentionChanged,
        });
        return AbsenceError.None;
    }

    // ------------------------------------------------------------------ reminder rhythm (set by HR)

    /// <summary>The reminder rhythm in force: the value HR has set, otherwise the configured default.</summary>
    public async Task<(int FirstAfterReturnDays, int EveryDays, int MaxReminders, bool IsDefault, DateTimeOffset? ChangedAt, Guid? ChangedBy)> GetReminderSettingsAsync(
        CancellationToken cancellationToken) =>
        await certificates.GetReminderSettingsAsync(cancellationToken) is { } set
            ? (set.FirstAfterReturnDays, set.EveryDays, set.MaxReminders, false, set.ChangedAt, set.ChangedBy)
            : (options.Certificates.FirstReminderAfterReturnDays, options.Certificates.ReminderEveryDays, options.Certificates.MaxReminders, true, null, null);

    /// <summary>
    /// HR sets the reminder rhythm. The first reminder is counted from the first day back, so it can never come earlier than that day
    /// (<paramref name="firstAfterReturnDays"/> is at least 0); the interval is at least one day; 0 reminders switch them off.
    /// </summary>
    public async Task<AbsenceError> SetReminderSettingsAsync(int firstAfterReturnDays, int everyDays, int maxReminders, Guid actorId, CancellationToken cancellationToken)
    {
        var limit = options.Certificates.MaxReminderSettingDays;
        if (firstAfterReturnDays < 0 || firstAfterReturnDays > limit || everyDays < 1 || everyDays > limit || maxReminders < 0 || maxReminders > limit)
        {
            return AbsenceError.InvalidReminderSettings;
        }

        var now = time.GetUtcNow();
        await certificates.SetReminderSettingsAsync(new CertificateReminderSettings(firstAfterReturnDays, everyDays, maxReminders, now, actorId), cancellationToken);
        certificates.Log(new CertificateAccessEntry
        {
            Id = Guid.CreateVersion7(now),
            At = now,
            ActorId = actorId,
            Action = CertificateAction.RemindersChanged,
        });
        return AbsenceError.None;
    }

    // ------------------------------------------------------------------ reading

    /// <summary>
    /// The documents of a sick note that <paramref name="actorId"/> may see as entries: all for reviewers and for those who may see the
    /// status (so they can tell that a certificate is there, never what is in it), otherwise only the own uploads.
    /// </summary>
    public async Task<IReadOnlyList<CertificateDocument>> ListAsync(
        Guid absenceId, Guid actorId, bool actorMayReview, bool actorMaySeeStatus, CancellationToken cancellationToken) =>
        [.. (await certificates.ForAbsenceAsync(absenceId, cancellationToken))
            .Where(d => actorMayReview || actorMaySeeStatus || d.UploadedBy == actorId)
            .OrderBy(d => d.UploadedAt)];

    /// <summary>
    /// The content of a document, only for its uploader or a reviewer; everybody else gets "not found". The caller decides beforehand
    /// whether somebody who may see that a certificate exists gets "not allowed" instead (see the endpoints). Every read is logged.
    /// </summary>
    public async Task<CertificateContent> ReadContentAsync(Guid documentId, Guid actorId, bool actorMayReview, CancellationToken cancellationToken)
    {
        if (await certificates.GetAsync(documentId, cancellationToken) is not { HasContent: true } document || (!actorMayReview && document.UploadedBy != actorId))
        {
            return CertificateContent.Fail(AbsenceError.NotFound);
        }

        if (await certificates.ReadContentAsync(document, cancellationToken) is not { } bytes)
        {
            return CertificateContent.Fail(AbsenceError.NotFound);
        }

        Log(document, CertificateAction.Downloaded, actorId, time.GetUtcNow());
        return new CertificateContent(AbsenceError.None, bytes, document.ContentType);
    }

    // ------------------------------------------------------------------ reminders and retention (background)

    /// <summary>
    /// Reminds people who have not uploaded a certificate (missing or rejected): the first reminder at the earliest on the first day
    /// back (the day after the sick note ends, plus the days HR has set), then at an interval, up to a maximum. A sick note without an
    /// end is not reminded yet. The reminder contains no data; the text says that something is waiting in the portal.
    /// </summary>
    /// <returns>How many reminders were created.</returns>
    public async Task<int> SendRemindersAsync(CancellationToken cancellationToken)
    {
        var settings = await GetReminderSettingsAsync(cancellationToken);
        var now = time.GetUtcNow();
        var today = absenceService.TodayLocal;
        var sent = 0;
        foreach (var absence in await certificates.WithoutCertificateAsync(settings.MaxReminders, cancellationToken))
        {
            if (absence.To is not { } end)
            {
                continue; // no end yet: the person may still be ill, so nobody is reminded before the return
            }

            var due = absence.LastReminderAt is { } last
                ? now - last >= TimeSpan.FromDays(settings.EveryDays)
                : today >= end.AddDays(1 + settings.FirstAfterReturnDays);
            if (!due || await people.GetAsync(absence.EmployeeId, cancellationToken) is not { } person || person.ExitDate is { } exit && exit < today)
            {
                continue;
            }

            absence.RemindersSent++;
            absence.LastReminderAt = now;
            Notify(ReminderTemplate, [absence.EmployeeId], absence, $"{absence.Id}:certificate-reminder:{absence.RemindersSent}", now);
            sent++;
        }

        return sent;
    }

    /// <summary>
    /// Deletes the files of sick notes that ended longer ago than the retention period in force (the value HR has set, otherwise the
    /// default), old access log entries and files in the storage that no document refers to.
    /// </summary>
    /// <returns>How many files were deleted because of the retention period.</returns>
    public async Task<int> PurgeExpiredAsync(CancellationToken cancellationToken)
    {
        var limits = options.Certificates;
        var now = time.GetUtcNow();
        var retention = (await GetRetentionAsync(cancellationToken)).Days;
        var expired = await certificates.ExpiredAsync(absenceService.TodayLocal.AddDays(-retention), cancellationToken);
        foreach (var document in expired)
        {
            await certificates.RemoveContentAsync(document, cancellationToken);
            document.ContentDeletedAt = now;
            Log(document, CertificateAction.PurgedByRetention, actorId: null, now);
        }

        await certificates.PurgeLogAsync(now.AddDays(-limits.AccessLogRetentionDays), cancellationToken);
        await certificates.RemoveOrphanedFilesAsync(now.AddMinutes(-limits.OrphanGraceMinutes), cancellationToken);
        return expired.Count;
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>The status of a sick note from its documents.</summary>
    public static CertificateStatus Derive(IEnumerable<CertificateDocument> documents)
    {
        var list = documents.ToList();
        if (list.Count == 0)
        {
            return CertificateStatus.Missing;
        }

        if (list.Any(d => d.State == CertificateDocumentState.Submitted))
        {
            return CertificateStatus.Submitted;
        }

        return list.OrderByDescending(d => d.UploadedAt).First().State == CertificateDocumentState.Rejected ? CertificateStatus.Rejected : CertificateStatus.Verified;
    }

    private void Log(CertificateDocument document, CertificateAction action, Guid? actorId, DateTimeOffset now) =>
        certificates.Log(new CertificateAccessEntry
        {
            Id = Guid.CreateVersion7(now),
            At = now,
            ActorId = actorId,
            Action = action,
            AbsenceId = document.AbsenceId,
            DocumentId = document.Id,
            EmployeeId = document.EmployeeId,
        });

    private void Notify(string templateKey, IReadOnlyList<Guid> recipients, Absence absence, string idempotencyKey, DateTimeOffset now) =>
        messages.Send(new SendNotification(
            Guid.CreateVersion7(now), Guid.CreateVersion7(now), now,
            DeterministicGuid.From(idempotencyKey),
            recipients, templateKey, new Dictionary<string, string>(), Channels, $"/absences/{absence.Id}"));
}
