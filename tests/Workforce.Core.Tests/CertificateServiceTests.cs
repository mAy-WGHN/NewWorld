using System.Text;
using Microsoft.Extensions.Time.Testing;
using Notifications.Contracts;
using Workforce.Contracts;
using Workforce.Core.Absences;
using Workforce.Core.Certificates;

namespace Workforce.Core.Tests;

/// <summary>
/// The medical certificate (AU) of a sick note: optional at reporting, uploaded afterwards, checked by HR, reminded, deleted by retention.
/// Fixed clock: Wednesday 2026-07-15, 10:00 local time (Europe/Berlin, summer time).
/// </summary>
public class CertificateServiceTests
{
    private static readonly Guid Erika = Guid.Parse("00000000-0000-0000-0000-0000000000e1");
    private static readonly Guid Max = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid Hanna = Guid.Parse("00000000-0000-0000-0000-0000000000d1");
    private static readonly Guid Store = Guid.Parse("00000000-0000-0000-0000-000000000017");

    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7 certificate");
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00];

    private readonly FakeStore _store = new();
    private readonly FakeCertificateStore _certificates;
    private readonly FakePeople _people = new();
    private readonly FakeEntitlements _entitlements = new();
    private readonly FakeResponsibilities _responsibilities = new();
    private readonly RecordingMessages _messages = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 7, 15, 8, 0, 0, TimeSpan.Zero));
    private readonly WorkforceOptions _options = new() { HolidayState = null };
    private FakeScanner _scanner = new();

    public CertificateServiceTests()
    {
        _certificates = new FakeCertificateStore(_store);
        _people.People[Erika] = new PersonInfo(Erika, "Erika Mustermann", Store, null);
        _people.People[Max] = new PersonInfo(Max, "Max Muster", Store, null);
        _people.People[Hanna] = new PersonInfo(Hanna, "Hanna HR", Store, null);
        _entitlements.Days[(Erika, 2026)] = 30;
        _responsibilities.Approvers[Erika] = [Max];
    }

    private AbsenceService Absences() => new(_store, _people, _entitlements, new FakeExtraVacation(), _responsibilities, _messages, _time, _options, new GermanHolidayCalendar());

    private CertificateService Service() => new(_store, _certificates, _people, _messages, Absences(), _scanner, _time, _options);

    private static DateOnly D(string date) => DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture);

    private async Task<Absence> SickNote(string from, string? to)
    {
        var sick = (await Absences().ReportSickAsync(Erika, D(from), to is null ? null : D(to), CancellationToken.None)).Absence!;
        _messages.Sent.Clear();
        return sick;
    }

    private async Task<CertificateDocument> Upload(Absence sick, string from, string to, Guid? uploader = null, byte[]? content = null)
    {
        var result = await Service().UploadAsync(sick.Id, uploader ?? Erika, content ?? Pdf, D(from), D(to), CancellationToken.None);
        result.Error.ShouldBe(AbsenceError.None);
        _messages.Sent.Clear();
        return result.Document!;
    }

    // ------------------------------------------------------------------ optional at reporting, upload afterwards

    [Fact]
    public async Task A_sick_note_without_certificate_starts_as_missing()
    {
        var sick = await SickNote("2026-07-15", "2026-07-17");

        sick.CertificateStatus.ShouldBe(CertificateStatus.Missing);
        sick.CertifiedThrough.ShouldBeNull();
        sick.RemindersSent.ShouldBe(0);
    }

    [Fact]
    public async Task Upload_afterwards_makes_the_certificate_submitted_and_stores_the_period_and_the_type_from_the_content()
    {
        var sick = await SickNote("2026-07-15", "2026-07-17");

        var result = await Service().UploadAsync(sick.Id, Erika, Pdf, D("2026-07-15"), D("2026-07-17"), CancellationToken.None);

        result.Error.ShouldBe(AbsenceError.None);
        sick.CertificateStatus.ShouldBe(CertificateStatus.Submitted);
        sick.CertifiedThrough.ShouldBe(D("2026-07-17"));
        var document = result.Document!;
        (document.EmployeeId, document.UploadedBy, document.State, document.ContentType, document.Length)
            .ShouldBe((Erika, Erika, CertificateDocumentState.Submitted, "application/pdf", Pdf.Length));
        (document.CertifiedFrom, document.CertifiedTo).ShouldBe((D("2026-07-15"), D("2026-07-17")));
        _certificates.Contents[document.Id].ShouldBe(Pdf);
    }

    [Fact]
    public async Task Upload_with_the_same_period_does_not_touch_the_absence_or_bc_but_is_logged_without_content()
    {
        var sick = await SickNote("2026-07-15", "2026-07-17");

        await Service().UploadAsync(sick.Id, Erika, Pdf, D("2026-07-15"), D("2026-07-17"), CancellationToken.None);

        sick.Version.ShouldBe(1);
        _messages.Sent.ShouldBeEmpty();
        var entry = _certificates.LogEntries.ShouldHaveSingleItem();
        (entry.Action, entry.ActorId, entry.AbsenceId, entry.EmployeeId).ShouldBe((CertificateAction.Uploaded, Erika, sick.Id, Erika));
        entry.At.ShouldBe(_time.GetUtcNow());
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData("jpeg")]
    [InlineData("png")]
    public async Task Pdf_jpeg_and_png_are_accepted_by_their_content(string kind)
    {
        var sick = await SickNote("2026-07-15", "2026-07-17");
        var bytes = kind switch { "pdf" => Pdf, "jpeg" => Jpeg, _ => Png };

        var result = await Service().UploadAsync(sick.Id, Erika, bytes, D("2026-07-15"), D("2026-07-17"), CancellationToken.None);

        result.Error.ShouldBe(AbsenceError.None);
        result.Document!.ContentType.ShouldBe(kind switch { "pdf" => "application/pdf", "jpeg" => "image/jpeg", _ => "image/png" });
    }

    // ------------------------------------------------------------------ the period keeps the absence up to date

    [Fact]
    public async Task Upload_gives_an_open_sick_note_its_end_and_sends_the_change_to_bc()
    {
        var sick = await SickNote("2026-07-15", null);

        (await Service().UploadAsync(sick.Id, Erika, Pdf, D("2026-07-15"), D("2026-07-24"), CancellationToken.None)).Error.ShouldBe(AbsenceError.None);

        sick.To.ShouldBe(D("2026-07-24"));
        sick.Version.ShouldBe(2);
        _messages.Of<SyncAbsenceToBc>().Single().Version.ShouldBe(2);
        _messages.Of<EmployeeAvailabilityChanged>().Single().To.ShouldBe(D("2026-07-24"));
    }

    [Fact]
    public async Task A_follow_up_certificate_extends_the_sick_note()
    {
        var sick = await SickNote("2026-07-15", "2026-07-17");
        await Upload(sick, "2026-07-15", "2026-07-17");

        (await Service().UploadAsync(sick.Id, Erika, Pdf, D("2026-07-18"), D("2026-07-24"), CancellationToken.None)).Error.ShouldBe(AbsenceError.None);

        sick.To.ShouldBe(D("2026-07-24"));
        sick.CertifiedThrough.ShouldBe(D("2026-07-24"));
        sick.Version.ShouldBe(2);
    }

    [Fact]
    public async Task A_certificate_that_ends_earlier_does_not_shorten_the_sick_note()
    {
        var sick = await SickNote("2026-07-15", "2026-07-24");

        await Service().UploadAsync(sick.Id, Erika, Pdf, D("2026-07-15"), D("2026-07-18"), CancellationToken.None);

        sick.To.ShouldBe(D("2026-07-24"));
        sick.Version.ShouldBe(1);
        sick.CertifiedThrough.ShouldBe(D("2026-07-18"));
    }

    [Fact]
    public async Task A_certificate_may_start_a_few_days_before_the_report_but_not_earlier()
    {
        var sick = await SickNote("2026-07-15", "2026-07-17");

        (await Service().UploadAsync(sick.Id, Erika, Pdf, D("2026-07-11"), D("2026-07-17"), CancellationToken.None)).Error.ShouldBe(AbsenceError.InvalidPeriod);
        (await Service().UploadAsync(sick.Id, Erika, Pdf, D("2026-07-12"), D("2026-07-17"), CancellationToken.None)).Error.ShouldBe(AbsenceError.None);

        sick.From.ShouldBe(D("2026-07-12"));
        sick.Version.ShouldBe(2);
    }

    [Theory]
    [InlineData("2026-07-18", "2026-07-25")] // starts after the day following the reported end: not continuous
    [InlineData("2026-07-10", "2026-07-14")] // ends before the sick note starts
    [InlineData("2026-07-17", "2026-07-15")] // end before start
    public async Task A_certificate_that_does_not_belong_to_the_sick_note_is_refused(string from, string to)
    {
        var sick = await SickNote("2026-07-15", "2026-07-16");

        var result = await Service().UploadAsync(sick.Id, Erika, Pdf, D(from), D(to), CancellationToken.None);

        result.Error.ShouldBe(AbsenceError.InvalidPeriod);
        _certificates.Documents.ShouldBeEmpty();
        sick.CertificateStatus.ShouldBe(CertificateStatus.Missing);
    }

    [Fact]
    public async Task A_certificate_for_more_than_a_year_is_refused()
    {
        var sick = await SickNote("2026-07-15", null);

        (await Service().UploadAsync(sick.Id, Erika, Pdf, D("2026-07-15"), D("2027-08-30"), CancellationToken.None)).Error.ShouldBe(AbsenceError.InvalidPeriod);
    }

    [Fact]
    public async Task Extending_into_approved_vacation_cancels_the_vacation_days_because_sickness_wins()
    {
        var requested = await Absences().RequestVacationAsync(Erika, D("2026-07-20"), D("2026-07-24"), CancellationToken.None);
        await Absences().DecideVacationAsync(requested.Absence!.Id, Max, approve: true, CancellationToken.None);
        var sick = await SickNote("2026-07-15", "2026-07-17");
        _messages.Sent.Clear();

        (await Service().UploadAsync(sick.Id, Erika, Pdf, D("2026-07-15"), D("2026-07-21"), CancellationToken.None)).Error.ShouldBe(AbsenceError.None);

        sick.To.ShouldBe(D("2026-07-21"));
        var vacation = _store.Absences.Single(a => a.Id == requested.Absence.Id);
        (vacation.From, vacation.To).ShouldBe((D("2026-07-22"), D("2026-07-24")));
    }

    [Fact]
    public async Task Extending_into_another_sick_note_is_refused_and_nothing_is_stored()
    {
        var first = await SickNote("2026-07-15", "2026-07-17");
        await SickNote("2026-07-20", "2026-07-22");

        var result = await Service().UploadAsync(first.Id, Erika, Pdf, D("2026-07-15"), D("2026-07-21"), CancellationToken.None);

        result.Error.ShouldBe(AbsenceError.Overlap);
        _certificates.Documents.ShouldBeEmpty();
        _certificates.LogEntries.ShouldBeEmpty();
        first.To.ShouldBe(D("2026-07-17"));
    }

    // ------------------------------------------------------------------ the file

    [Fact]
    public async Task Content_that_is_not_pdf_jpeg_or_png_is_refused_whatever_the_name_says()
    {
        var sick = await SickNote("2026-07-15", "2026-07-17");

        var result = await Service().UploadAsync(sick.Id, Erika, Encoding.ASCII.GetBytes("MZ evil.exe renamed to certificate.pdf"), D("2026-07-15"), D("2026-07-17"), CancellationToken.None);

        result.Error.ShouldBe(AbsenceError.UnsupportedFileType);
        _certificates.Documents.ShouldBeEmpty();
        _scanner.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task An_empty_or_too_large_file_is_refused()
    {
        var sick = await SickNote("2026-07-15", "2026-07-17");
        _options.Certificates.MaxFileBytes = 100;
        var tooLarge = new byte[101];
        Pdf.CopyTo(tooLarge, 0);

        (await Service().UploadAsync(sick.Id, Erika, ReadOnlyMemory<byte>.Empty, D("2026-07-15"), D("2026-07-17"), CancellationToken.None)).Error.ShouldBe(AbsenceError.FileTooLarge);
        (await Service().UploadAsync(sick.Id, Erika, tooLarge, D("2026-07-15"), D("2026-07-17"), CancellationToken.None)).Error.ShouldBe(AbsenceError.FileTooLarge);
    }

    [Fact]
    public async Task A_file_that_fails_the_malware_check_is_refused_and_not_stored()
    {
        _scanner = new FakeScanner(clean: false);
        var sick = await SickNote("2026-07-15", "2026-07-17");

        var result = await Service().UploadAsync(sick.Id, Erika, Pdf, D("2026-07-15"), D("2026-07-17"), CancellationToken.None);

        result.Error.ShouldBe(AbsenceError.FileRejected);
        _certificates.Documents.ShouldBeEmpty();
        _certificates.Contents.ShouldBeEmpty();
        sick.CertificateStatus.ShouldBe(CertificateStatus.Missing);
    }

    [Fact]
    public async Task There_is_a_limit_of_documents_per_sick_note_but_rejected_ones_do_not_count()
    {
        _options.Certificates.MaxDocumentsPerAbsence = 2;
        var sick = await SickNote("2026-07-15", "2026-07-17");
        var first = await Upload(sick, "2026-07-15", "2026-07-17");
        await Upload(sick, "2026-07-15", "2026-07-17");

        (await Service().UploadAsync(sick.Id, Erika, Pdf, D("2026-07-15"), D("2026-07-17"), CancellationToken.None)).Error.ShouldBe(AbsenceError.TooManyDocuments);
        await Service().RejectAsync(first.Id, Hanna, CertificateRejectionReason.Unreadable, CancellationToken.None);
        (await Service().UploadAsync(sick.Id, Erika, Pdf, D("2026-07-15"), D("2026-07-17"), CancellationToken.None)).Error.ShouldBe(AbsenceError.None);
    }

    [Fact]
    public async Task Only_a_reported_sick_note_takes_a_certificate()
    {
        var vacation = (await Absences().RequestVacationAsync(Erika, D("2026-07-20"), D("2026-07-24"), CancellationToken.None)).Absence!;

        (await Service().UploadAsync(vacation.Id, Erika, Pdf, D("2026-07-20"), D("2026-07-24"), CancellationToken.None)).Error.ShouldBe(AbsenceError.InvalidState);
        (await Service().UploadAsync(Guid.NewGuid(), Erika, Pdf, D("2026-07-20"), D("2026-07-24"), CancellationToken.None)).Error.ShouldBe(AbsenceError.NotFound);
    }

    // ------------------------------------------------------------------ review by HR

    [Fact]
    public async Task HR_verifies_a_submitted_certificate()
    {
        var sick = await SickNote("2026-07-15", "2026-07-17");
        var document = await Upload(sick, "2026-07-15", "2026-07-17");

        var result = await Service().VerifyAsync(document.Id, Hanna, CancellationToken.None);

        result.Error.ShouldBe(AbsenceError.None);
        (document.State, document.ReviewedBy, document.ReviewedAt).ShouldBe((CertificateDocumentState.Verified, Hanna, _time.GetUtcNow()));
        sick.CertificateStatus.ShouldBe(CertificateStatus.Verified);
        _certificates.LogEntries.Last().Action.ShouldBe(CertificateAction.Verified);
        _certificates.Contents.ShouldContainKey(document.Id);
    }

    [Fact]
    public async Task Nobody_reviews_a_certificate_about_themselves()
    {
        var sick = await SickNote("2026-07-15", "2026-07-17");
        var document = await Upload(sick, "2026-07-15", "2026-07-17");

        (await Service().VerifyAsync(document.Id, Erika, CancellationToken.None)).Error.ShouldBe(AbsenceError.NotAllowed);
        (await Service().RejectAsync(document.Id, Erika, CertificateRejectionReason.Unreadable, CancellationToken.None)).Error.ShouldBe(AbsenceError.NotAllowed);
        document.State.ShouldBe(CertificateDocumentState.Submitted);
    }

    [Fact]
    public async Task A_decided_certificate_cannot_be_decided_again()
    {
        var sick = await SickNote("2026-07-15", "2026-07-17");
        var document = await Upload(sick, "2026-07-15", "2026-07-17");
        await Service().VerifyAsync(document.Id, Hanna, CancellationToken.None);

        (await Service().VerifyAsync(document.Id, Hanna, CancellationToken.None)).Error.ShouldBe(AbsenceError.InvalidState);
        (await Service().RejectAsync(document.Id, Hanna, CertificateRejectionReason.WrongPeriod, CancellationToken.None)).Error.ShouldBe(AbsenceError.InvalidState);
        (await Service().VerifyAsync(Guid.NewGuid(), Hanna, CancellationToken.None)).Error.ShouldBe(AbsenceError.NotFound);
    }

    [Fact]
    public async Task Rejecting_keeps_the_file_asks_for_a_new_upload_and_notifies_without_data()
    {
        var sick = await SickNote("2026-07-15", "2026-07-17");
        var document = await Upload(sick, "2026-07-15", "2026-07-17", uploader: Max);

        var result = await Service().RejectAsync(document.Id, Hanna, CertificateRejectionReason.WrongPerson, CancellationToken.None);

        result.Error.ShouldBe(AbsenceError.None);
        (document.State, document.RejectionReason, document.HasContent).ShouldBe((CertificateDocumentState.Rejected, CertificateRejectionReason.WrongPerson, true));
        _certificates.Contents.ShouldContainKey(document.Id);
        sick.CertificateStatus.ShouldBe(CertificateStatus.Rejected);
        var notification = _messages.Of<SendNotification>().Single();
        notification.TemplateKey.ShouldBe(CertificateService.RejectedTemplate);
        notification.RecipientEmployeeIds.ShouldBe([Erika, Max], ignoreOrder: true);
        notification.Channels.ShouldBe([NotificationChannel.InApp, NotificationChannel.Push], ignoreOrder: true);
        notification.Data.ShouldBeEmpty();
    }

    [Fact]
    public async Task Rejection_needs_one_of_the_defined_reasons()
    {
        var sick = await SickNote("2026-07-15", "2026-07-17");
        var document = await Upload(sick, "2026-07-15", "2026-07-17");

        (await Service().RejectAsync(document.Id, Hanna, CertificateRejectionReason.Unknown, CancellationToken.None)).Error.ShouldBe(AbsenceError.InvalidState);
        (await Service().RejectAsync(document.Id, Hanna, (CertificateRejectionReason)99, CancellationToken.None)).Error.ShouldBe(AbsenceError.InvalidState);
        document.State.ShouldBe(CertificateDocumentState.Submitted);
    }

    [Fact]
    public async Task After_a_rejection_the_person_uploads_again()
    {
        var sick = await SickNote("2026-07-15", "2026-07-17");
        var first = await Upload(sick, "2026-07-15", "2026-07-17");
        await Service().RejectAsync(first.Id, Hanna, CertificateRejectionReason.Unreadable, CancellationToken.None);

        (await Service().UploadAsync(sick.Id, Erika, Jpeg, D("2026-07-15"), D("2026-07-17"), CancellationToken.None)).Error.ShouldBe(AbsenceError.None);

        sick.CertificateStatus.ShouldBe(CertificateStatus.Submitted);
    }

    // ------------------------------------------------------------------ the uploader

    [Fact]
    public async Task The_uploader_can_withdraw_an_unchecked_upload()
    {
        var sick = await SickNote("2026-07-15", "2026-07-17");
        var document = await Upload(sick, "2026-07-15", "2026-07-17", uploader: Max);

        var result = await Service().DeleteAsync(document.Id, Max, actorMayDelete: false, CancellationToken.None);

        result.Error.ShouldBe(AbsenceError.None);
        _certificates.Documents.ShouldBeEmpty();
        _certificates.Contents.ShouldBeEmpty();
        sick.CertificateStatus.ShouldBe(CertificateStatus.Missing);
        _certificates.LogEntries.Last().Action.ShouldBe(CertificateAction.DeletedByUploader);
    }

    [Fact]
    public async Task Only_the_uploader_withdraws_and_not_after_the_check()
    {
        var sick = await SickNote("2026-07-15", "2026-07-17");
        var document = await Upload(sick, "2026-07-15", "2026-07-17", uploader: Max);

        (await Service().DeleteAsync(document.Id, Erika, actorMayDelete: false, CancellationToken.None)).Error.ShouldBe(AbsenceError.NotFound);
        (await Service().DeleteAsync(document.Id, Hanna, actorMayDelete: false, CancellationToken.None)).Error.ShouldBe(AbsenceError.NotFound);
        await Service().VerifyAsync(document.Id, Hanna, CancellationToken.None);
        (await Service().DeleteAsync(document.Id, Max, actorMayDelete: false, CancellationToken.None)).Error.ShouldBe(AbsenceError.InvalidState);
    }

    // ------------------------------------------------------------------ HR deletes by hand

    [Fact]
    public async Task HR_can_delete_an_unchecked_upload_of_somebody_else_completely()
    {
        var sick = await SickNote("2026-07-15", "2026-07-17");
        var document = await Upload(sick, "2026-07-15", "2026-07-17", uploader: Max);

        var result = await Service().DeleteAsync(document.Id, Hanna, actorMayDelete: true, CancellationToken.None);

        result.Error.ShouldBe(AbsenceError.None);
        _certificates.Documents.ShouldBeEmpty();
        _certificates.Contents.ShouldBeEmpty();
        sick.CertificateStatus.ShouldBe(CertificateStatus.Missing);
        var entry = _certificates.LogEntries.Last();
        (entry.Action, entry.ActorId).ShouldBe((CertificateAction.DeletedByPersonnel, (Guid?)Hanna));
    }

    [Fact]
    public async Task HR_can_delete_a_rejected_upload()
    {
        var sick = await SickNote("2026-07-15", "2026-07-17");
        var document = await Upload(sick, "2026-07-15", "2026-07-17");
        await Service().RejectAsync(document.Id, Hanna, CertificateRejectionReason.Unreadable, CancellationToken.None);

        (await Service().DeleteAsync(document.Id, Hanna, actorMayDelete: true, CancellationToken.None)).Error.ShouldBe(AbsenceError.None);

        _certificates.Documents.ShouldBeEmpty();
        _certificates.Contents.ShouldBeEmpty();
    }

    [Fact]
    public async Task HR_deleting_a_verified_certificate_removes_the_file_but_keeps_the_status_so_nobody_is_reminded_again()
    {
        var sick = await SickNote("2026-07-15", "2026-07-30");
        var document = await Upload(sick, "2026-07-15", "2026-07-30");
        await Service().VerifyAsync(document.Id, Hanna, CancellationToken.None);

        (await Service().DeleteAsync(document.Id, Hanna, actorMayDelete: true, CancellationToken.None)).Error.ShouldBe(AbsenceError.None);

        _certificates.Contents.ShouldBeEmpty();
        document.HasContent.ShouldBeFalse();
        _certificates.Documents.ShouldContain(document);
        sick.CertificateStatus.ShouldBe(CertificateStatus.Verified);
        _time.Advance(TimeSpan.FromDays(5));
        (await Service().SendRemindersAsync(CancellationToken.None)).ShouldBe(0);
        (await Service().DeleteAsync(document.Id, Hanna, actorMayDelete: true, CancellationToken.None)).Error.ShouldBe(AbsenceError.InvalidState);
    }

    [Fact]
    public async Task Nobody_deletes_a_document_about_themselves_that_somebody_else_uploaded_only_because_they_hold_the_privilege()
    {
        var sick = await SickNote("2026-07-15", "2026-07-17");
        var document = await Upload(sick, "2026-07-15", "2026-07-17", uploader: Max);

        (await Service().DeleteAsync(document.Id, Erika, actorMayDelete: true, CancellationToken.None)).Error.ShouldBe(AbsenceError.NotAllowed);

        _certificates.Documents.Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_file_is_stored_under_a_random_name_that_has_nothing_to_do_with_the_document_or_the_person()
    {
        var sick = await SickNote("2026-07-15", "2026-07-17");
        var first = await Upload(sick, "2026-07-15", "2026-07-17");
        var second = await Upload(sick, "2026-07-15", "2026-07-17", content: Jpeg);

        new[] { first.StorageId, second.StorageId, first.Id, second.Id }.Distinct().Count().ShouldBe(4);
        first.StorageId.ShouldNotBe(Guid.Empty);
        first.StorageId.ToString("N").ShouldNotContain(Erika.ToString("N"));
    }

    // ------------------------------------------------------------------ who sees the file

    [Fact]
    public async Task Only_the_uploader_and_reviewers_can_read_the_content_and_every_read_is_logged()
    {
        var sick = await SickNote("2026-07-15", "2026-07-17");
        var document = await Upload(sick, "2026-07-15", "2026-07-17", uploader: Max);

        var byUploader = await Service().ReadContentAsync(document.Id, Max, actorMayReview: false, CancellationToken.None);
        var byReviewer = await Service().ReadContentAsync(document.Id, Hanna, actorMayReview: true, CancellationToken.None);
        var byEmployee = await Service().ReadContentAsync(document.Id, Erika, actorMayReview: false, CancellationToken.None);
        var byColleague = await Service().ReadContentAsync(document.Id, Guid.NewGuid(), actorMayReview: false, CancellationToken.None);

        byUploader.Error.ShouldBe(AbsenceError.None);
        byUploader.Bytes.ShouldBe(Pdf);
        byUploader.ContentType.ShouldBe("application/pdf");
        byReviewer.Error.ShouldBe(AbsenceError.None);
        byEmployee.Error.ShouldBe(AbsenceError.NotFound);
        byColleague.Error.ShouldBe(AbsenceError.NotFound);
        _certificates.LogEntries.Where(e => e.Action == CertificateAction.Downloaded).Select(e => e.ActorId).ShouldBe([Max, Hanna]);
    }

    [Fact]
    public async Task A_rejected_file_can_still_be_read_by_HR_and_the_uploader_until_retention_deletes_it()
    {
        var sick = await SickNote("2026-07-15", "2026-07-17");
        var document = await Upload(sick, "2026-07-15", "2026-07-17");
        await Service().RejectAsync(document.Id, Hanna, CertificateRejectionReason.Unreadable, CancellationToken.None);

        (await Service().ReadContentAsync(document.Id, Hanna, actorMayReview: true, CancellationToken.None)).Error.ShouldBe(AbsenceError.None);
        (await Service().ReadContentAsync(document.Id, Erika, actorMayReview: false, CancellationToken.None)).Error.ShouldBe(AbsenceError.None);

        _time.Advance(TimeSpan.FromDays(370));
        (await Service().PurgeExpiredAsync(CancellationToken.None)).ShouldBe(1);

        (await Service().ReadContentAsync(document.Id, Hanna, actorMayReview: true, CancellationToken.None)).Error.ShouldBe(AbsenceError.NotFound);
        document.State.ShouldBe(CertificateDocumentState.Rejected);
    }

    [Fact]
    public async Task Reviewers_list_all_documents_of_a_sick_note_everyone_else_only_their_own()
    {
        var sick = await SickNote("2026-07-15", "2026-07-17");
        var byEmployee = await Upload(sick, "2026-07-15", "2026-07-17", uploader: Erika);
        var byManager = await Upload(sick, "2026-07-15", "2026-07-17", uploader: Max);

        (await Service().ListAsync(sick.Id, Hanna, actorMayReview: true, actorMaySeeStatus: true, CancellationToken.None)).Select(d => d.Id).ShouldBe([byEmployee.Id, byManager.Id]);
        (await Service().ListAsync(sick.Id, Max, actorMayReview: false, actorMaySeeStatus: false, CancellationToken.None)).Select(d => d.Id).ShouldBe([byManager.Id]);
        (await Service().ListAsync(sick.Id, Guid.NewGuid(), actorMayReview: false, actorMaySeeStatus: false, CancellationToken.None)).ShouldBeEmpty();
        // Somebody who may see the status (e.g. a store manager) sees that the documents are there, not more.
        (await Service().ListAsync(sick.Id, Guid.NewGuid(), actorMayReview: false, actorMaySeeStatus: true, CancellationToken.None)).Select(d => d.Id).ShouldBe([byEmployee.Id, byManager.Id]);
    }

    // ------------------------------------------------------------------ the status

    [Fact]
    public void The_status_follows_the_documents()
    {
        CertificateDocument Doc(CertificateDocumentState state, int minute) => new()
        {
            Id = Guid.NewGuid(),
            UploadedAt = new DateTimeOffset(2026, 7, 15, 8, minute, 0, TimeSpan.Zero),
            State = state,
        };

        CertificateService.Derive([]).ShouldBe(CertificateStatus.Missing);
        CertificateService.Derive([Doc(CertificateDocumentState.Submitted, 0)]).ShouldBe(CertificateStatus.Submitted);
        CertificateService.Derive([Doc(CertificateDocumentState.Verified, 0)]).ShouldBe(CertificateStatus.Verified);
        CertificateService.Derive([Doc(CertificateDocumentState.Rejected, 0)]).ShouldBe(CertificateStatus.Rejected);
        CertificateService.Derive([Doc(CertificateDocumentState.Verified, 0), Doc(CertificateDocumentState.Submitted, 1)]).ShouldBe(CertificateStatus.Submitted);
        CertificateService.Derive([Doc(CertificateDocumentState.Rejected, 0), Doc(CertificateDocumentState.Verified, 1)]).ShouldBe(CertificateStatus.Verified);
        CertificateService.Derive([Doc(CertificateDocumentState.Verified, 0), Doc(CertificateDocumentState.Rejected, 1)]).ShouldBe(CertificateStatus.Rejected);
    }

    // ------------------------------------------------------------------ reminders

    [Fact]
    public async Task The_first_reminder_comes_on_the_first_day_back_and_only_for_a_missing_certificate()
    {
        var sick = await SickNote("2026-07-15", "2026-07-16");

        (await Service().SendRemindersAsync(CancellationToken.None)).ShouldBe(0); // day of the report
        _time.Advance(TimeSpan.FromDays(1));
        (await Service().SendRemindersAsync(CancellationToken.None)).ShouldBe(0); // last day of the sick note
        _time.Advance(TimeSpan.FromDays(1));
        (await Service().SendRemindersAsync(CancellationToken.None)).ShouldBe(1); // first day back

        sick.RemindersSent.ShouldBe(1);
        sick.LastReminderAt.ShouldBe(_time.GetUtcNow());
        var reminder = _messages.Of<SendNotification>().Single();
        reminder.TemplateKey.ShouldBe(CertificateService.ReminderTemplate);
        reminder.RecipientEmployeeIds.ShouldBe([Erika]);
        reminder.Channels.ShouldBe([NotificationChannel.InApp, NotificationChannel.Push], ignoreOrder: true);
        reminder.Data.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_sick_note_without_an_end_is_not_reminded()
    {
        await SickNote("2026-07-15", null);
        _time.Advance(TimeSpan.FromDays(30));

        (await Service().SendRemindersAsync(CancellationToken.None)).ShouldBe(0);
    }

    [Fact]
    public async Task HR_sets_the_reminder_rhythm_and_it_never_starts_before_the_first_day_back()
    {
        var sick = await SickNote("2026-07-15", "2026-07-16");
        (await Service().SetReminderSettingsAsync(-1, 2, 5, Hanna, CancellationToken.None)).ShouldBe(AbsenceError.InvalidReminderSettings);
        (await Service().SetReminderSettingsAsync(0, 0, 5, Hanna, CancellationToken.None)).ShouldBe(AbsenceError.InvalidReminderSettings);
        (await Service().SetReminderSettingsAsync(0, 2, -1, Hanna, CancellationToken.None)).ShouldBe(AbsenceError.InvalidReminderSettings);
        (await Service().SetReminderSettingsAsync(0, 2, 1000, Hanna, CancellationToken.None)).ShouldBe(AbsenceError.InvalidReminderSettings);
        (await Service().GetReminderSettingsAsync(CancellationToken.None)).IsDefault.ShouldBeTrue();

        (await Service().SetReminderSettingsAsync(3, 1, 2, Hanna, CancellationToken.None)).ShouldBe(AbsenceError.None);
        var set = await Service().GetReminderSettingsAsync(CancellationToken.None);
        (set.FirstAfterReturnDays, set.EveryDays, set.MaxReminders, set.IsDefault, set.ChangedBy).ShouldBe((3, 1, 2, false, (Guid?)Hanna));
        _time.Advance(TimeSpan.FromDays(4)); // 2026-07-19: only the 3rd day back is the first possible day
        (await Service().SendRemindersAsync(CancellationToken.None)).ShouldBe(0);
        _time.Advance(TimeSpan.FromDays(1));
        (await Service().SendRemindersAsync(CancellationToken.None)).ShouldBe(1);
        _time.Advance(TimeSpan.FromDays(1)); // interval 1 day now
        (await Service().SendRemindersAsync(CancellationToken.None)).ShouldBe(1);
        _time.Advance(TimeSpan.FromDays(1)); // maximum of 2 reached
        (await Service().SendRemindersAsync(CancellationToken.None)).ShouldBe(0);
        sick.RemindersSent.ShouldBe(2);
    }

    [Fact]
    public async Task Reminders_repeat_at_the_interval_and_stop_at_the_maximum()
    {
        _options.Certificates.MaxReminders = 3;
        var sick = await SickNote("2026-07-15", "2026-07-16");
        _time.Advance(TimeSpan.FromDays(2));

        var total = 0;
        for (var day = 0; day < 20; day++)
        {
            total += await Service().SendRemindersAsync(CancellationToken.None);
            _time.Advance(TimeSpan.FromDays(1));
        }

        total.ShouldBe(3);
        sick.RemindersSent.ShouldBe(3);
        _messages.Of<SendNotification>().Count().ShouldBe(3);
        _messages.Of<SendNotification>().Select(n => n.NotificationId).Distinct().Count().ShouldBe(3);
    }

    [Fact]
    public async Task Reminders_do_not_change_the_version_or_go_to_bc()
    {
        var sick = await SickNote("2026-07-15", "2026-07-16");
        _time.Advance(TimeSpan.FromDays(2));

        await Service().SendRemindersAsync(CancellationToken.None);

        sick.Version.ShouldBe(1);
        _messages.Sent.ShouldAllBe(m => m is SendNotification);
    }

    [Theory]
    [InlineData(CertificateDocumentState.Submitted)]
    [InlineData(CertificateDocumentState.Verified)]
    public async Task A_person_who_has_uploaded_is_not_reminded(CertificateDocumentState state)
    {
        var sick = await SickNote("2026-07-15", "2026-07-30");
        var document = await Upload(sick, "2026-07-15", "2026-07-30");
        if (state == CertificateDocumentState.Verified)
        {
            await Service().VerifyAsync(document.Id, Hanna, CancellationToken.None);
        }

        _time.Advance(TimeSpan.FromDays(10));

        (await Service().SendRemindersAsync(CancellationToken.None)).ShouldBe(0);
    }

    [Fact]
    public async Task A_rejected_certificate_is_reminded_again()
    {
        var sick = await SickNote("2026-07-15", "2026-07-30");
        var document = await Upload(sick, "2026-07-15", "2026-07-30");
        await Service().RejectAsync(document.Id, Hanna, CertificateRejectionReason.Unreadable, CancellationToken.None);
        _time.Advance(TimeSpan.FromDays(17)); // the day after the end

        (await Service().SendRemindersAsync(CancellationToken.None)).ShouldBe(1);
    }

    [Fact]
    public async Task Nobody_is_reminded_after_leaving_the_company()
    {
        await SickNote("2026-07-15", "2026-07-30");
        _people.People[Erika] = _people.People[Erika] with { ExitDate = D("2026-07-16") };
        _time.Advance(TimeSpan.FromDays(17));

        (await Service().SendRemindersAsync(CancellationToken.None)).ShouldBe(0);
    }

    // ------------------------------------------------------------------ retention

    [Fact]
    public async Task Files_are_deleted_after_the_retention_period_and_the_status_stays()
    {
        var sick = await SickNote("2026-07-15", "2026-07-17");
        var document = await Upload(sick, "2026-07-15", "2026-07-17");
        await Service().VerifyAsync(document.Id, Hanna, CancellationToken.None);

        // The sick note ended on 2026-07-17; 365 days later (2027-07-17) the file goes.
        _time.Advance(TimeSpan.FromDays(364)); // 2027-07-14
        (await Service().PurgeExpiredAsync(CancellationToken.None)).ShouldBe(0);
        _time.Advance(TimeSpan.FromDays(3)); // 2027-07-17
        (await Service().PurgeExpiredAsync(CancellationToken.None)).ShouldBe(1);

        _certificates.Contents.ShouldBeEmpty();
        document.HasContent.ShouldBeFalse();
        sick.CertificateStatus.ShouldBe(CertificateStatus.Verified);
        var purge = _certificates.LogEntries.Last();
        (purge.Action, purge.ActorId).ShouldBe((CertificateAction.PurgedByRetention, (Guid?)null));
    }

    [Fact]
    public async Task The_files_of_an_open_sick_note_are_kept()
    {
        var sick = await SickNote("2026-07-15", null);
        var document = await Upload(sick, "2026-07-15", "2026-07-17");
        sick.To = null; // still sick, e.g. waiting for the follow-up certificate
        _ = document;

        _time.Advance(TimeSpan.FromDays(400));

        (await Service().PurgeExpiredAsync(CancellationToken.None)).ShouldBe(0);
        _certificates.Contents.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Without_a_value_from_HR_the_configured_default_applies()
    {
        var retention = await Service().GetRetentionAsync(CancellationToken.None);

        (retention.Days, retention.IsDefault, retention.ChangedAt, retention.ChangedBy).ShouldBe((365, true, null, null));
    }

    [Fact]
    public async Task HR_sets_the_retention_period_and_it_applies_to_the_files_already_stored()
    {
        var sick = await SickNote("2026-07-15", "2026-07-17");
        await Upload(sick, "2026-07-15", "2026-07-17");

        (await Service().SetRetentionAsync(30, Hanna, CancellationToken.None)).ShouldBe(AbsenceError.None);

        var retention = await Service().GetRetentionAsync(CancellationToken.None);
        (retention.Days, retention.IsDefault, retention.ChangedBy).ShouldBe((30, false, (Guid?)Hanna));
        retention.ChangedAt.ShouldBe(_time.GetUtcNow());
        var entry = _certificates.LogEntries.Last();
        (entry.Action, entry.ActorId, entry.DocumentId).ShouldBe((CertificateAction.RetentionChanged, (Guid?)Hanna, Guid.Empty));

        // The sick note ended on 2026-07-17: after 30 days (2026-08-16) the file goes, much earlier than with the default of 365.
        _time.Advance(TimeSpan.FromDays(32)); // 2026-08-16
        (await Service().PurgeExpiredAsync(CancellationToken.None)).ShouldBe(1);
    }

    [Fact]
    public async Task A_retention_period_of_zero_deletes_on_the_end_day()
    {
        var sick = await SickNote("2026-07-15", "2026-07-17");
        await Upload(sick, "2026-07-15", "2026-07-17");
        await Service().SetRetentionAsync(0, Hanna, CancellationToken.None);

        _time.Advance(TimeSpan.FromDays(1)); // 2026-07-16, the sick note still runs
        (await Service().PurgeExpiredAsync(CancellationToken.None)).ShouldBe(0);
        _time.Advance(TimeSpan.FromDays(1)); // 2026-07-17, the last day
        (await Service().PurgeExpiredAsync(CancellationToken.None)).ShouldBe(1);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3651)]
    public async Task A_retention_period_outside_the_allowed_range_is_refused_and_changes_nothing(int days)
    {
        (await Service().SetRetentionAsync(days, Hanna, CancellationToken.None)).ShouldBe(AbsenceError.InvalidRetention);

        _certificates.Retention.ShouldBeNull();
        _certificates.LogEntries.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_maintenance_also_sweeps_files_that_no_document_refers_to_but_leaves_recent_ones_alone()
    {
        await Service().PurgeExpiredAsync(CancellationToken.None);

        _certificates.OrphanSweeps.ShouldBe([_time.GetUtcNow().AddMinutes(-60)]);
    }

    [Fact]
    public async Task Old_access_log_entries_are_deleted_after_their_retention()
    {
        var sick = await SickNote("2026-07-15", "2026-07-17");
        await Upload(sick, "2026-07-15", "2026-07-17");
        _time.Advance(TimeSpan.FromDays(366));

        await Service().PurgeExpiredAsync(CancellationToken.None);

        _certificates.LogEntries.Count(e => e.Action == CertificateAction.Uploaded).ShouldBe(0);
    }
}
