using System.Net;
using BcIntegration.Contracts;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Shared.Authorization;
using Shared.Messaging;
using Shared.Testing;
using Workforce.Api;
using Workforce.Core.Absences;
using Workforce.Core.Certificates;
using Workforce.Infrastructure;
using Workforce.Infrastructure.Certificates;
using static Workforce.Api.Tests.WorkforceApi;

namespace Workforce.Api.Tests;

/// <summary>
/// The medical certificate (AU) of a sick note through the real API and database: optional at reporting, uploaded afterwards, only the
/// uploader and HR can read the file, HR checks it and filters by status, people are reminded, and the file is encrypted at rest.
/// Clock: Friday 2026-10-23, 12:00 UTC.
/// </summary>
public sealed class CertificateEndpointTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7 the certificate of Erika");
    private static readonly ReportSickRequest Sick = new(new DateOnly(2026, 10, 23), new DateOnly(2026, 10, 25));

    private static string Own(Guid absenceId) => $"/api/me/absences/{absenceId}/certificates";

    private static string Docs(Guid absenceId) => $"/api/absences/{absenceId}/certificates";

    private static string For(Guid employeeId, Guid absenceId) => $"/api/employees/{employeeId}/absences/{absenceId}/certificates";

    private static async Task<AbsenceDto> ReportedAsync(WorkforceApi api, ReportSickRequest? request = null) =>
        await api.PostAbsenceAsync("/api/me/absences/sick", Erika, request ?? Sick);

    private static async Task<HttpResponseMessage> GetAsync(WorkforceApi api, string url, Guid? person) => await api.SendAsync(HttpMethod.Get, url, person);

    /// <summary>All privileges everywhere except one (HR without that privilege).</summary>
    private static TestGrant[] AllExcept(string key) =>
        [.. AbsencePrivileges.Catalog.Concat(CertificatePrivileges.Catalog).Where(c => c.Key != key).Select(c => TestGrant.Everywhere(c.Key))];

    private static async Task<int> StoredFilesAsync(WorkforceApi api) =>
        (await api.Services.GetRequiredService<ICertificateFileStorage>().ListAsync(CancellationToken.None)).Count;

    // ------------------------------------------------------------------ report first, upload later

    [Fact]
    public async Task Reporting_needs_no_certificate_and_the_upload_follows_later()
    {
        await using var api = await StartAsync(postgres);
        var absence = await ReportedAsync(api);

        absence.CertificateStatus.ShouldBe("Missing");

        var document = await api.UploadOkAsync(Own(absence.Id), Erika, Pdf, "2026-10-23", "2026-10-25");

        document.State.ShouldBe("Submitted");
        document.ContentType.ShouldBe("application/pdf");
        var own = (await api.GetAsync<List<AbsenceDto>>("/api/me/absences", Erika)).Single(a => a.Id == absence.Id);
        (own.CertificateStatus, own.CertifiedThrough).ShouldBe(("Submitted", new DateOnly(2026, 10, 25)));
    }

    [Fact]
    public async Task The_period_from_the_certificate_gives_an_open_sick_note_its_end_and_goes_to_bc()
    {
        await using var api = await StartAsync(postgres);
        var open = await ReportedAsync(api, new ReportSickRequest(new DateOnly(2026, 10, 23), null));

        await api.UploadOkAsync(Own(open.Id), Erika, Pdf, "2026-10-23", "2026-10-30");

        var absence = (await api.GetAsync<List<AbsenceDto>>("/api/me/absences", Erika)).Single(a => a.Id == open.Id);
        absence.To.ShouldBe(new DateOnly(2026, 10, 30));
        var sync = (await api.OutboxAsync()).Where(m => m.MessageType == "SyncAbsenceToBc").ToList();
        sync.Count.ShouldBe(2); // the report and the change of the period
        sync.Last().Payload.ShouldContain("2026-10-30");
    }

    [Fact]
    public async Task The_file_is_never_in_the_outbox_and_lies_encrypted_in_the_separate_storage_under_a_random_name()
    {
        await using var api = await StartAsync(postgres);
        var absence = await ReportedAsync(api);
        var document = await api.UploadOkAsync(Own(absence.Id), Erika, Pdf, "2026-10-23", "2026-10-25");

        (await api.OutboxAsync()).ShouldAllBe(m => !m.Payload.Contains("certificate of Erika") && !m.Payload.Contains(document.Id.ToString()));
        using var scope = api.Services.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<WorkforceDbContext>().CertificateDocuments.AsNoTracking().SingleAsync(d => d.Id == document.Id);
        var storage = (FileSystemCertificateStorage)api.Services.GetRequiredService<ICertificateFileStorage>();

        // One file, named by the random storage id: no extension, nothing of the original name, the person or the date.
        var path = Directory.EnumerateFiles(storage.Root).Single();
        Path.GetFileName(path).ShouldBe(row.StorageId.ToString("N"));
        Path.HasExtension(path).ShouldBeFalse();
        row.StorageId.ShouldNotBe(row.Id);
        var bytes = await File.ReadAllBytesAsync(path);
        bytes.Length.ShouldBeGreaterThan(Pdf.Length);
        Encoding.ASCII.GetString(bytes).ShouldNotContain("certificate of Erika");
        Encoding.ASCII.GetString(bytes).ShouldNotContain("%PDF");
        // The storage lies outside the application directory, and the database has no table for file contents any more.
        Path.GetFullPath(storage.Root).StartsWith(Path.GetFullPath(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase).ShouldBeFalse();
        (await scope.ServiceProvider.GetRequiredService<WorkforceDbContext>().Database
            .SqlQueryRaw<int>("select count(*)::int as \"Value\" from information_schema.tables where table_name = 'certificate_files'").SingleAsync()).ShouldBe(0);
    }

    // ------------------------------------------------------------------ the file

    [Fact]
    public async Task Content_that_is_not_pdf_jpeg_or_png_is_refused_whatever_the_file_name_says()
    {
        await using var api = await StartAsync(postgres);
        var absence = await ReportedAsync(api);

        using var response = await api.UploadCertificateAsync(Own(absence.Id), Erika, Encoding.ASCII.GetBytes("<script>alert(1)</script>"), "2026-10-23", "2026-10-25", "au.pdf");

        response.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task A_file_over_the_limit_is_refused()
    {
        await using var api = await StartAsync(postgres);
        var absence = await ReportedAsync(api);
        var large = new byte[10 * 1024 * 1024 + 1];
        Pdf.CopyTo(large, 0);

        using var response = await api.UploadCertificateAsync(Own(absence.Id), Erika, large, "2026-10-23", "2026-10-25");

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
    }

    [Theory]
    [InlineData("2026-10-25", "2026-10-23")] // end before start
    [InlineData("2026-12-01", "2026-12-05")] // does not belong to the sick note
    [InlineData("23.10.2026", "25.10.2026")] // not the agreed date format
    public async Task A_period_that_does_not_fit_is_refused_and_nothing_is_stored(string from, string to)
    {
        await using var api = await StartAsync(postgres);
        var absence = await ReportedAsync(api);

        using var response = await api.UploadCertificateAsync(Own(absence.Id), Erika, Pdf, from, to);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        using var scope = api.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<WorkforceDbContext>().CertificateDocuments.AnyAsync()).ShouldBeFalse();
    }

    // ------------------------------------------------------------------ who may upload

    [Fact]
    public async Task Someone_elses_sick_note_looks_like_a_missing_one_for_the_own_upload()
    {
        await using var api = await StartAsync(postgres);
        var absence = await ReportedAsync(api);

        using var response = await api.UploadCertificateAsync(Own(absence.Id), Ben, Pdf, "2026-10-23", "2026-10-25");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Upload_needs_the_privilege_and_a_signed_in_person()
    {
        await using var api = await StartAsync(postgres);
        var absence = await ReportedAsync(api);
        api.Services.GetRequiredService<IPermissionCache>().Grant(Erika, new TestGrant(AbsencePrivileges.ReadOwn));

        (await api.UploadCertificateAsync(Own(absence.Id), Erika, Pdf, "2026-10-23", "2026-10-25")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await api.UploadCertificateAsync(Own(absence.Id), null, Pdf, "2026-10-23", "2026-10-25")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await api.UploadCertificateAsync(For(Erika, absence.Id), null, Pdf, "2026-10-23", "2026-10-25")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_store_manager_uploads_for_a_person_in_reach_but_not_for_anyone_else()
    {
        await using var api = await StartAsync(postgres);
        var absence = await ReportedAsync(api);

        var byManager = await api.UploadOkAsync(For(Erika, absence.Id), Max, Pdf, "2026-10-23", "2026-10-25");
        using var otherStore = await api.UploadCertificateAsync(For(Erika, absence.Id), Lena, Pdf, "2026-10-23", "2026-10-25");

        byManager.UploadedBy.ShouldBe(Max);
        otherStore.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ------------------------------------------------------------------ who sees the file

    [Fact]
    public async Task Only_the_uploader_and_HR_can_open_the_file_nobody_else_not_even_the_manager_of_the_store()
    {
        await using var api = await StartAsync(postgres);
        var absence = await ReportedAsync(api);
        var document = await api.UploadOkAsync(Own(absence.Id), Erika, Pdf, "2026-10-23", "2026-10-25");
        var url = $"{Docs(absence.Id)}/{document.Id}/content";

        using var byUploader = await GetAsync(api, url, Erika);
        using var byHr = await GetAsync(api, url, Hanna);
        using var byManager = await GetAsync(api, url, Max);        // reads the reason of the absence, not the document
        using var byColleague = await GetAsync(api, url, Ben);
        using var byOtherStore = await GetAsync(api, url, Lena);
        using var anonymous = await GetAsync(api, url, null);

        byUploader.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await byUploader.Content.ReadAsByteArrayAsync()).ShouldBe(Pdf);
        byHr.StatusCode.ShouldBe(HttpStatusCode.OK);
        byManager.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        byColleague.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        byOtherStore.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_download_is_a_neutral_attachment_that_is_not_cached_or_interpreted()
    {
        await using var api = await StartAsync(postgres);
        var absence = await ReportedAsync(api);
        var document = await api.UploadOkAsync(Own(absence.Id), Erika, Pdf, "2026-10-23", "2026-10-25");

        using var response = await GetAsync(api, $"{Docs(absence.Id)}/{document.Id}/content", Erika);

        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        response.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
        response.Content.Headers.ContentDisposition.FileName.ShouldBe("certificate.pdf");
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        response.Headers.GetValues("Content-Security-Policy").Single().ShouldContain("sandbox");
    }

    [Fact]
    public async Task The_manager_who_uploaded_sees_only_that_upload_the_person_sees_theirs_HR_sees_all()
    {
        await using var api = await StartAsync(postgres);
        var absence = await ReportedAsync(api);
        var byEmployee = await api.UploadOkAsync(Own(absence.Id), Erika, Pdf, "2026-10-23", "2026-10-25");
        var byManager = await api.UploadOkAsync(For(Erika, absence.Id), Max, Pdf, "2026-10-23", "2026-10-25");

        (await api.GetAsync<List<CertificateDto>>(Docs(absence.Id), Hanna)).Select(d => d.Id).ShouldBe([byEmployee.Id, byManager.Id], ignoreOrder: true);
        (await api.GetAsync<List<CertificateDto>>(Docs(absence.Id), Erika)).Select(d => d.Id).ShouldBe([byEmployee.Id]);
        (await api.GetAsync<List<CertificateDto>>(Docs(absence.Id), Max)).Select(d => d.Id).ShouldBe([byManager.Id]);
        (await api.GetAsync<List<CertificateDto>>(Docs(absence.Id), Ben)).ShouldBeEmpty();
        using var unknown = await GetAsync(api, Docs(Guid.NewGuid()), Hanna);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var forbiddenContent = await GetAsync(api, $"{Docs(absence.Id)}/{byEmployee.Id}/content", Max);
        forbiddenContent.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Somebody_who_may_see_the_status_sees_that_a_certificate_is_there_but_is_denied_when_opening_it()
    {
        await using var api = await StartAsync(postgres);
        var absence = await ReportedAsync(api);
        var document = await api.UploadOkAsync(Own(absence.Id), Erika, Pdf, "2026-10-23", "2026-10-25");
        api.Grant(Max, new TestGrant(CertificatePrivileges.ReadStatus, [StoreA], [Erika, Ben, Max]));
        var url = $"{Docs(absence.Id)}/{document.Id}";

        // The store manager sees the entry (state, period, that there is a file) but not who uploaded it, and cannot open it.
        var listed = (await api.GetAsync<List<CertificateDto>>(Docs(absence.Id), Max)).Single();
        (listed.State, listed.HasContent, listed.CanOpen, listed.UploadedBy).ShouldBe(("Submitted", true, false, (Guid?)null));
        (await GetAsync(api, $"{url}/content", Max)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await api.SendAsync(HttpMethod.Post, $"{url}/verify", Max)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await api.SendAsync(HttpMethod.Post, $"{url}/reject", Max, new { reason = "Unreadable" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await api.SendAsync(HttpMethod.Delete, url, Max)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // A colleague without the status privilege does not even learn that it exists.
        (await api.GetAsync<List<CertificateDto>>(Docs(absence.Id), Ben)).ShouldBeEmpty();
        (await GetAsync(api, $"{url}/content", Ben)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await api.SendAsync(HttpMethod.Post, $"{url}/verify", Ben)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await api.SendAsync(HttpMethod.Delete, url, Ben)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The uploader and HR can open it and see more.
        var own = (await api.GetAsync<List<CertificateDto>>(Docs(absence.Id), Erika)).Single();
        (own.CanOpen, own.UploadedBy).ShouldBe((true, (Guid?)Erika));
        (await api.GetAsync<List<CertificateDto>>(Docs(absence.Id), Hanna)).Single().CanOpen.ShouldBeTrue();
        (await GetAsync(api, $"{url}/content", Hanna)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ------------------------------------------------------------------ the status

    [Fact]
    public async Task The_status_is_visible_to_the_person_and_to_holders_of_the_status_privilege_only()
    {
        await using var api = await StartAsync(postgres);
        var absence = await ReportedAsync(api);

        var asManager = (await api.GetAsync<List<AbsenceDto>>($"/api/employees/{Erika}/absences", Max)).Single(a => a.Id == absence.Id);
        asManager.Type.ShouldBe("Sickness");           // the manager reads the reason
        asManager.CertificateStatus.ShouldBeNull();    // but not the certificate status without the privilege

        api.Grant(Max, new TestGrant(AbsencePrivileges.ReadReason, [StoreA], [Erika, Ben, Max]), new TestGrant(CertificatePrivileges.ReadStatus, [StoreA], [Erika, Ben, Max]));
        var withPrivilege = (await api.GetAsync<List<AbsenceDto>>($"/api/employees/{Erika}/absences", Max)).Single(a => a.Id == absence.Id);
        withPrivilege.CertificateStatus.ShouldBe("Missing");

        var colleague = (await api.GetAsync<List<StoreAbsenceDto>>($"/api/stores/{StoreA}/absences?from=2026-10-23&to=2026-10-31", Ben)).Single();
        colleague.Status.ShouldBe("Absent");
    }

    // ------------------------------------------------------------------ review by HR

    [Fact]
    public async Task HR_verifies_the_certificate_and_nobody_else_can()
    {
        await using var api = await StartAsync(postgres);
        var absence = await ReportedAsync(api);
        var document = await api.UploadOkAsync(Own(absence.Id), Erika, Pdf, "2026-10-23", "2026-10-25");
        var verify = $"{Docs(absence.Id)}/{document.Id}/verify";

        (await api.SendAsync(HttpMethod.Post, verify, Erika)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await api.SendAsync(HttpMethod.Post, verify, Max)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await api.SendAsync(HttpMethod.Post, verify, null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var ok = await api.SendAsync(HttpMethod.Post, verify, Hanna);

        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ok.Content.ReadFromJsonAsync<CertificateDto>())!.State.ShouldBe("Verified");
        (await api.GetAsync<List<AbsenceDto>>("/api/me/absences", Erika)).Single(a => a.Id == absence.Id).CertificateStatus.ShouldBe("Verified");
        (await api.SendAsync(HttpMethod.Post, verify, Hanna)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task HR_rejects_with_a_reason_the_file_stays_and_the_person_is_asked_to_upload_again()
    {
        await using var api = await StartAsync(postgres);
        var absence = await ReportedAsync(api);
        var document = await api.UploadOkAsync(Own(absence.Id), Erika, Pdf, "2026-10-23", "2026-10-25");
        var url = $"{Docs(absence.Id)}/{document.Id}";

        (await api.SendAsync(HttpMethod.Post, $"{url}/reject", Hanna, new { reason = "because I say so" })).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await api.SendAsync(HttpMethod.Post, $"{url}/reject", Max, new { reason = "Unreadable" })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var rejected = await api.SendAsync(HttpMethod.Post, $"{url}/reject", Hanna, new { reason = "Unreadable" });

        rejected.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = (await rejected.Content.ReadFromJsonAsync<CertificateDto>())!;
        (dto.State, dto.RejectionReason, dto.HasContent).ShouldBe(("Rejected", "Unreadable", true));
        (await GetAsync(api, $"{url}/content", Hanna)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(api, $"{url}/content", Erika)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await api.GetAsync<List<AbsenceDto>>("/api/me/absences", Erika)).Single(a => a.Id == absence.Id).CertificateStatus.ShouldBe("Rejected");
        var notification = (await api.OutboxAsync()).Last(m => m.MessageType == "SendNotification").Payload;
        notification.ShouldContain("workforce.sick-note-certificate-rejected.employee");
        notification.ShouldNotContain("Sickness", Case.Insensitive);
        (await StoredFilesAsync(api)).ShouldBe(1); // kept until retention

        (await api.UploadOkAsync(Own(absence.Id), Erika, Pdf, "2026-10-23", "2026-10-25")).State.ShouldBe("Submitted");
    }

    [Fact]
    public async Task The_uploader_withdraws_an_unchecked_upload_nobody_else_can()
    {
        await using var api = await StartAsync(postgres);
        var absence = await ReportedAsync(api);
        var document = await api.UploadOkAsync(For(Erika, absence.Id), Max, Pdf, "2026-10-23", "2026-10-25");
        var url = $"{Docs(absence.Id)}/{document.Id}";

        (await api.SendAsync(HttpMethod.Delete, url, Erika)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        api.Grant(Hanna, AllExcept(CertificatePrivileges.Delete));
        (await api.SendAsync(HttpMethod.Delete, url, Hanna)).StatusCode.ShouldBe(HttpStatusCode.Forbidden); // sees that it exists, but may not delete without the privilege
        (await api.SendAsync(HttpMethod.Delete, url, null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await api.SendAsync(HttpMethod.Delete, url, Max)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await api.GetAsync<List<AbsenceDto>>("/api/me/absences", Erika)).Single(a => a.Id == absence.Id).CertificateStatus.ShouldBe("Missing");
        (await StoredFilesAsync(api)).ShouldBe(0);
    }

    // ------------------------------------------------------------------ HR deletes by hand, HR sets the retention

    [Fact]
    public async Task HR_with_the_delete_privilege_deletes_any_document_and_the_file_is_gone_from_the_storage()
    {
        await using var api = await StartAsync(postgres);
        var absence = await ReportedAsync(api);
        var document = await api.UploadOkAsync(Own(absence.Id), Erika, Pdf, "2026-10-23", "2026-10-25");
        await api.SendAsync(HttpMethod.Post, $"{Docs(absence.Id)}/{document.Id}/reject", Hanna, new { reason = "Unreadable" });
        var url = $"{Docs(absence.Id)}/{document.Id}";
        (await StoredFilesAsync(api)).ShouldBe(1);

        (await api.SendAsync(HttpMethod.Delete, url, Ben)).StatusCode.ShouldBe(HttpStatusCode.NotFound); // no right to know about it
        (await api.SendAsync(HttpMethod.Delete, url, Lena)).StatusCode.ShouldBe(HttpStatusCode.NotFound); // other store
        (await api.SendAsync(HttpMethod.Delete, url, Hanna)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await StoredFilesAsync(api)).ShouldBe(0);
        (await GetAsync(api, Docs(absence.Id), Hanna)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await api.GetAsync<List<CertificateDto>>(Docs(absence.Id), Hanna)).ShouldBeEmpty();
        (await api.GetAsync<List<AbsenceDto>>("/api/me/absences", Erika)).Single(a => a.Id == absence.Id).CertificateStatus.ShouldBe("Missing");
        var log = await api.GetAsync<PageDto<CertificateAccessEntryDto>>($"/api/certificates/access-log?absenceId={absence.Id}", Hanna);
        log.Items.ShouldContain(e => e.Action == "DeletedByPersonnel" && e.ActorId == Hanna);
    }

    [Fact]
    public async Task HR_deleting_a_verified_certificate_removes_the_file_and_keeps_the_status()
    {
        await using var api = await StartAsync(postgres);
        var absence = await ReportedAsync(api);
        var document = await api.UploadOkAsync(Own(absence.Id), Erika, Pdf, "2026-10-23", "2026-10-25");
        var url = $"{Docs(absence.Id)}/{document.Id}";
        await api.SendAsync(HttpMethod.Post, $"{url}/verify", Hanna);

        (await api.SendAsync(HttpMethod.Delete, url, Hanna)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await StoredFilesAsync(api)).ShouldBe(0);
        (await GetAsync(api, $"{url}/content", Hanna)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await api.GetAsync<List<AbsenceDto>>("/api/me/absences", Erika)).Single(a => a.Id == absence.Id).CertificateStatus.ShouldBe("Verified");
        var entry = (await api.GetAsync<List<CertificateDto>>(Docs(absence.Id), Hanna)).Single();
        (entry.State, entry.HasContent, entry.CanOpen).ShouldBe(("Verified", false, false));
    }

    [Fact]
    public async Task Only_HR_with_the_privilege_reads_and_sets_the_retention_period_and_it_applies_to_stored_files()
    {
        await using var api = await StartAsync(postgres);
        var absence = await ReportedAsync(api);
        await api.UploadOkAsync(Own(absence.Id), Erika, Pdf, "2026-10-23", "2026-10-25");

        var initial = await api.GetAsync<CertificateRetentionDto>("/api/certificates/retention", Hanna);
        (initial.RetentionAfterEndDays, initial.IsDefault).ShouldBe((365, true));
        foreach (var person in new Guid?[] { Max, Erika, null })
        {
            var expected = person is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
            (await api.SendAsync(HttpMethod.Get, "/api/certificates/retention", person)).StatusCode.ShouldBe(expected);
            (await api.SendAsync(HttpMethod.Put, "/api/certificates/retention", person, new { retentionAfterEndDays = 1 })).StatusCode.ShouldBe(expected);
        }

        (await api.SendAsync(HttpMethod.Put, "/api/certificates/retention", Hanna, new { retentionAfterEndDays = -1 })).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await api.SendAsync(HttpMethod.Put, "/api/certificates/retention", Hanna, new { retentionAfterEndDays = 4000 })).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        using var set = await api.SendAsync(HttpMethod.Put, "/api/certificates/retention", Hanna, new { retentionAfterEndDays = 30 });
        set.StatusCode.ShouldBe(HttpStatusCode.OK);
        var saved = (await set.Content.ReadFromJsonAsync<CertificateRetentionDto>())!;
        (saved.RetentionAfterEndDays, saved.IsDefault, saved.ChangedBy).ShouldBe((30, false, (Guid?)Hanna));
        (await api.GetAsync<CertificateRetentionDto>("/api/certificates/retention", Hanna)).RetentionAfterEndDays.ShouldBe(30);
        (await api.GetAsync<PageDto<CertificateAccessEntryDto>>("/api/certificates/access-log", Hanna)).Items.ShouldContain(e => e.Action == "RetentionChanged" && e.ActorId == Hanna);

        // The sick note ended on 2026-10-25: after 40 days the file is gone, with the default of 90 it would still be there.
        api.Services.GetRequiredService<FakeTimeProvider>().Advance(TimeSpan.FromDays(40));
        await api.Services.GetServices<IHostedService>().OfType<CertificateMaintenanceService>().Single().RunOnceAsync(CancellationToken.None);
        (await StoredFilesAsync(api)).ShouldBe(0);
    }

    [Fact]
    public async Task The_maintenance_removes_files_that_no_document_refers_to_but_keeps_the_ones_that_do()
    {
        await using var api = await StartAsync(postgres);
        var open = await ReportedAsync(api, new ReportSickRequest(new DateOnly(2026, 10, 23), null));
        await api.UploadOkAsync(Own(open.Id), Erika, Pdf, "2026-10-23", "2026-10-25");
        (await api.SendAsync(HttpMethod.Put, "/api/certificates/retention", Hanna, new { retentionAfterEndDays = 3650 })).StatusCode.ShouldBe(HttpStatusCode.OK); // the document is not due
        var storage = api.Services.GetRequiredService<ICertificateFileStorage>();
        await storage.WriteAsync(Guid.NewGuid(), [1, 2, 3], CancellationToken.None); // e.g. left behind by a save that failed
        (await StoredFilesAsync(api)).ShouldBe(2);

        api.Services.GetRequiredService<FakeTimeProvider>().Advance(TimeSpan.FromDays(400)); // the file times are real, so the grace period is over
        await api.Services.GetServices<IHostedService>().OfType<CertificateMaintenanceService>().Single().RunOnceAsync(CancellationToken.None);

        (await StoredFilesAsync(api)).ShouldBe(1);
        (await GetAsync(api, $"{Docs(open.Id)}", Hanna)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ------------------------------------------------------------------ the HR overview

    [Fact]
    public async Task HR_filters_sick_notes_by_certificate_status_and_only_sees_people_in_reach()
    {
        await using var api = await StartAsync(postgres);
        var erika = await ReportedAsync(api);
        var lena = await api.PostAbsenceAsync("/api/me/absences/sick", Lena, Sick);
        var document = await api.UploadOkAsync(Own(erika.Id), Erika, Pdf, "2026-10-23", "2026-10-25");

        var missing = await api.GetAsync<PageDto<SickNoteOverviewDto>>("/api/sick-notes?certificateStatus=Missing", Hanna);
        var submitted = await api.GetAsync<PageDto<SickNoteOverviewDto>>("/api/sick-notes?certificateStatus=Submitted", Hanna);
        var both = await api.GetAsync<PageDto<SickNoteOverviewDto>>("/api/sick-notes?certificateStatus=Missing,Submitted", Hanna);

        missing.Items.Select(i => i.AbsenceId).ShouldBe([lena.Id]);
        submitted.Items.Select(i => i.AbsenceId).ShouldBe([erika.Id]);
        submitted.Items.Single().EmployeeName.ShouldBe("Erika Mustermann");
        both.Total.ShouldBe(2);

        await api.SendAsync(HttpMethod.Post, $"{Docs(erika.Id)}/{document.Id}/verify", Hanna);
        (await api.GetAsync<PageDto<SickNoteOverviewDto>>("/api/sick-notes?certificateStatus=Verified&storeId=" + StoreA, Hanna)).Items.Select(i => i.AbsenceId).ShouldBe([erika.Id]);

        // A manager with the status privilege for store B only: sees store B, not store A.
        api.Grant(Lena, new TestGrant(CertificatePrivileges.ReadStatus, [StoreB], [Lena]));
        (await api.GetAsync<PageDto<SickNoteOverviewDto>>("/api/sick-notes", Lena)).Items.Select(i => i.AbsenceId).ShouldBe([lena.Id]);
    }

    [Fact]
    public async Task The_overview_needs_the_status_privilege_and_validates_its_filter()
    {
        await using var api = await StartAsync(postgres);

        (await api.SendAsync(HttpMethod.Get, "/api/sick-notes", Max)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await api.SendAsync(HttpMethod.Get, "/api/sick-notes", Erika)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await api.SendAsync(HttpMethod.Get, "/api/sick-notes", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await api.SendAsync(HttpMethod.Get, "/api/sick-notes?certificateStatus=Whatever", Hanna)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    // ------------------------------------------------------------------ the access log

    [Fact]
    public async Task Every_upload_download_and_decision_is_logged_without_content_and_only_auditors_read_the_log()
    {
        await using var api = await StartAsync(postgres);
        var absence = await ReportedAsync(api);
        var document = await api.UploadOkAsync(Own(absence.Id), Erika, Pdf, "2026-10-23", "2026-10-25");
        await GetAsync(api, $"{Docs(absence.Id)}/{document.Id}/content", Hanna);
        await api.SendAsync(HttpMethod.Post, $"{Docs(absence.Id)}/{document.Id}/verify", Hanna);

        var log = await api.GetAsync<PageDto<CertificateAccessEntryDto>>($"/api/certificates/access-log?absenceId={absence.Id}", Hanna);

        log.Items.Select(e => (e.Action, e.ActorId)).ShouldBe([("Verified", (Guid?)Hanna), ("Downloaded", Hanna), ("Uploaded", Erika)], ignoreOrder: true); // same clock tick in the test
        (await api.SendAsync(HttpMethod.Get, "/api/certificates/access-log", Max)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await api.SendAsync(HttpMethod.Get, "/api/certificates/access-log", Erika)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ------------------------------------------------------------------ reminders and retention (background)

    [Fact]
    public async Task A_missing_certificate_is_reminded_and_the_reminder_carries_no_data()
    {
        await using var api = await StartAsync(postgres);
        var absence = await ReportedAsync(api);
        var maintenance = api.Services.GetServices<IHostedService>().OfType<CertificateMaintenanceService>().Single();

        await maintenance.RunOnceAsync(CancellationToken.None);
        (await api.OutboxAsync()).Count(m => m.Payload.Contains("certificate-reminder")).ShouldBe(0); // too early

        api.Services.GetRequiredService<FakeTimeProvider>().Advance(TimeSpan.FromDays(4)); // first day back: the day after the sick note ends
        await maintenance.RunOnceAsync(CancellationToken.None);
        await maintenance.RunOnceAsync(CancellationToken.None); // the second round the same day does not repeat it

        var reminders = (await api.OutboxAsync()).Where(m => m.Payload.Contains("workforce.sick-note-certificate-reminder.employee")).ToList();
        // The background service ticks too when the clock moves; a repeat carries the same notification id, so it is delivered once.
        reminders.Select(m => System.Text.Json.JsonDocument.Parse(m.Payload).RootElement.GetProperty("notificationId").GetGuid()).Distinct().Count().ShouldBe(1);
        reminders[0].Payload.ShouldContain(Erika.ToString());
        reminders[0].Payload.ShouldNotContain("Sickness", Case.Insensitive);
        reminders[0].Payload.ShouldNotContain("Mustermann");
        _ = absence;
    }

    [Fact]
    public async Task Only_HR_with_the_privilege_reads_and_sets_the_reminder_rhythm()
    {
        await using var api = await StartAsync(postgres);

        var initial = await api.GetAsync<CertificateReminderSettingsDto>("/api/certificates/reminders", Hanna);
        (initial.FirstReminderAfterReturnDays, initial.ReminderEveryDays, initial.MaxReminders, initial.IsDefault).ShouldBe((0, 2, 5, true));
        foreach (var person in new Guid?[] { Max, Erika, null })
        {
            var expected = person is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
            (await api.SendAsync(HttpMethod.Get, "/api/certificates/reminders", person)).StatusCode.ShouldBe(expected);
            (await api.SendAsync(HttpMethod.Put, "/api/certificates/reminders", person, new { firstReminderAfterReturnDays = 1, reminderEveryDays = 1, maxReminders = 1 })).StatusCode.ShouldBe(expected);
        }

        // Before the first day back is not possible: negative values and an interval of 0 are rejected.
        (await api.SendAsync(HttpMethod.Put, "/api/certificates/reminders", Hanna, new { firstReminderAfterReturnDays = -1, reminderEveryDays = 2, maxReminders = 5 })).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await api.SendAsync(HttpMethod.Put, "/api/certificates/reminders", Hanna, new { firstReminderAfterReturnDays = 0, reminderEveryDays = 0, maxReminders = 5 })).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        using var set = await api.SendAsync(HttpMethod.Put, "/api/certificates/reminders", Hanna, new { firstReminderAfterReturnDays = 2, reminderEveryDays = 3, maxReminders = 4 });
        set.StatusCode.ShouldBe(HttpStatusCode.OK);
        var saved = (await set.Content.ReadFromJsonAsync<CertificateReminderSettingsDto>())!;
        (saved.FirstReminderAfterReturnDays, saved.ReminderEveryDays, saved.MaxReminders, saved.IsDefault, saved.ChangedBy).ShouldBe((2, 3, 4, false, (Guid?)Hanna));
        (await api.GetAsync<PageDto<CertificateAccessEntryDto>>("/api/certificates/access-log", Hanna)).Items.ShouldContain(e => e.Action == "RemindersChanged" && e.ActorId == Hanna);
    }

    [Fact]
    public async Task Files_are_deleted_after_the_retention_period()
    {
        await using var api = await StartAsync(postgres);
        var absence = await ReportedAsync(api);
        var document = await api.UploadOkAsync(Own(absence.Id), Erika, Pdf, "2026-10-23", "2026-10-25");
        await api.SendAsync(HttpMethod.Post, $"{Docs(absence.Id)}/{document.Id}/verify", Hanna);
        var maintenance = api.Services.GetServices<IHostedService>().OfType<CertificateMaintenanceService>().Single();

        api.Services.GetRequiredService<FakeTimeProvider>().Advance(TimeSpan.FromDays(370));
        await maintenance.RunOnceAsync(CancellationToken.None);

        (await GetAsync(api, $"{Docs(absence.Id)}/{document.Id}/content", Hanna)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkforceDbContext>();
        (await StoredFilesAsync(api)).ShouldBe(0);
        (await db.CertificateDocuments.SingleAsync()).HasContent.ShouldBeFalse();
        (await api.GetAsync<List<AbsenceDto>>("/api/me/absences", Erika)).Single(a => a.Id == absence.Id).CertificateStatus.ShouldBe("Verified");
    }

    // ------------------------------------------------------------------ erasure

    [Fact]
    public async Task Erasure_removes_the_documents_files_and_log_of_the_person_and_anonymises_where_they_only_uploaded()
    {
        await using var api = await StartAsync(postgres);
        var absence = await ReportedAsync(api);
        var byManager = await api.UploadOkAsync(For(Erika, absence.Id), Max, Pdf, "2026-10-23", "2026-10-25");
        await GetAsync(api, $"{Docs(absence.Id)}/{byManager.Id}/content", Max);
        var ben = await api.PostAbsenceAsync("/api/me/absences/sick", Ben, Sick);
        await api.UploadOkAsync(For(Ben, ben.Id), Max, Pdf, "2026-10-23", "2026-10-25");

        await api.DeliverAsync(new EmployeeDataErasureRequested(Guid.NewGuid(), Guid.NewGuid(), Start, Erika));

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkforceDbContext>();
        (await db.CertificateDocuments.AnyAsync(d => d.EmployeeId == Erika)).ShouldBeFalse();
        (await db.CertificateAccessLog.AnyAsync(e => e.EmployeeId == Erika)).ShouldBeFalse();
        (await db.CertificateDocuments.CountAsync(d => d.EmployeeId == Ben)).ShouldBe(1);
        (await StoredFilesAsync(api)).ShouldBe(1);

        // Now the manager is erased: the document about Ben stays (it is about Ben), the manager's id is gone from it and from the log.
        await api.DeliverAsync(new EmployeeDataErasureRequested(Guid.NewGuid(), Guid.NewGuid(), Start, Max));
        using var scope2 = api.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<WorkforceDbContext>();
        (await db2.CertificateDocuments.AnyAsync(d => d.UploadedBy == Max)).ShouldBeFalse();
        (await db2.CertificateAccessLog.AnyAsync(e => e.ActorId == Max)).ShouldBeFalse();
        (await db2.CertificateDocuments.CountAsync(d => d.EmployeeId == Ben)).ShouldBe(1);
    }
}
