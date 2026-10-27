using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Shared.Authorization;
using Workforce.Contracts;
using Workforce.Core.Absences;
using Workforce.Core.Certificates;
using Workforce.Infrastructure;
using Workforce.Infrastructure.Absences;

namespace Workforce.Api;

/// <summary>
/// The medical certificate (AU) of a sick note. The strictest data of the service: the file is readable only by its uploader and by
/// holders of <see cref="CertificatePrivileges.Review"/> for that person (HR). Who may see the status
/// (<see cref="CertificatePrivileges.ReadStatus"/>, e.g. store managers) can tell that a certificate is there but gets "permission denied"
/// (403) when trying to open, check or delete it. Everybody who may not even see the status gets "not found": the existence is not revealed.
/// Every upload, download and decision is logged without content. HR (holders of the delete privilege) can delete files by hand and set
/// after how many days the files are deleted automatically.
/// </summary>
public static class CertificateEndpoints
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    public static WebApplication MapCertificateEndpoints(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<WorkforceOptions>();
        var maxBytes = options.Certificates.MaxFileBytes;

        // Upload: the sick note can be reported first; the certificate follows when it is there (also several times: follow-up certificates).
        app.MapPost("/api/me/absences/{absenceId:guid}/certificates", async (
                Guid absenceId, HttpRequest request, ClaimsPrincipal user, WorkforceDbContext db, CertificateService service, WorkforceUnitOfWork uow, CancellationToken ct) =>
            {
                if (Endpoints.Me(user) is not { } me)
                {
                    return Results.Forbid();
                }

                // Someone else's sick note looks like a missing one.
                return await db.Absences.AsNoTracking().AnyAsync(a => a.Id == absenceId && a.EmployeeId == me, ct)
                    ? await UploadAsync(request, absenceId, me, service, uow, maxBytes, ct)
                    : Results.NotFound();
            })
            .RequireAuthorization(AppPolicies.Privilege(CertificatePrivileges.UploadOwn));

        // A store manager or HR uploads for a person in their reach.
        app.MapPost("/api/employees/{employeeId:guid}/absences/{absenceId:guid}/certificates", async (
                Guid employeeId, Guid absenceId, HttpRequest request, ClaimsPrincipal user, IAuthorizationService auth, WorkforceDbContext db,
                CertificateService service, WorkforceUnitOfWork uow, CancellationToken ct) =>
            {
                if (Endpoints.Me(user) is not { } me)
                {
                    return Results.Forbid();
                }

                if (!await Endpoints.Allows(auth, user, new EmployeeResource(employeeId), CertificatePrivileges.UploadFor)
                    || !await db.Absences.AsNoTracking().AnyAsync(a => a.Id == absenceId && a.EmployeeId == employeeId, ct))
                {
                    return Results.NotFound();
                }

                return await UploadAsync(request, absenceId, me, service, uow, maxBytes, ct);
            })
            .RequireAuthorization();

        // The documents of a sick note that the caller may see: all for HR, otherwise only their own uploads.
        app.MapGet("/api/absences/{absenceId:guid}/certificates", async (
                Guid absenceId, ClaimsPrincipal user, IAuthorizationService auth, WorkforceDbContext db, CertificateService service, CancellationToken ct) =>
            {
                if (Endpoints.Me(user) is not { } me)
                {
                    return Results.Forbid();
                }

                if (await db.Absences.AsNoTracking().Where(a => a.Id == absenceId).Select(a => (Guid?)a.EmployeeId).FirstOrDefaultAsync(ct) is not { } owner)
                {
                    return Results.NotFound();
                }

                var review = await Endpoints.Allows(auth, user, new EmployeeResource(owner), CertificatePrivileges.Review);
                var status = review || await Endpoints.Allows(auth, user, new EmployeeResource(owner), CertificatePrivileges.ReadStatus);
                return Results.Ok((await service.ListAsync(absenceId, me, review, status, ct)).Select(d => ToDto(d, me, review)));
            })
            .RequireAuthorization();

        app.MapGet("/api/absences/{absenceId:guid}/certificates/{documentId:guid}/content", async (
                Guid absenceId, Guid documentId, HttpContext http, ClaimsPrincipal user, IAuthorizationService auth, WorkforceDbContext db,
                CertificateService service, WorkforceUnitOfWork uow, CancellationToken ct) =>
            {
                if (Endpoints.Me(user) is not { } me)
                {
                    return Results.Forbid();
                }

                if (await db.CertificateDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId && d.AbsenceId == absenceId, ct) is not { } document)
                {
                    return Results.NotFound();
                }

                var review = await Endpoints.Allows(auth, user, new EmployeeResource(document.EmployeeId), CertificatePrivileges.Review);
                if (!review && document.UploadedBy != me)
                {
                    return await Denied(auth, user, document.EmployeeId);
                }

                var content = await uow.RunAsync(() => service.ReadContentAsync(documentId, me, review, ct), c => c.Error == AbsenceError.None, ct);
                if (content.Error != AbsenceError.None)
                {
                    return Results.NotFound();
                }

                // A health document: never cached, never interpreted by the browser, no scripts if it is opened.
                http.Response.Headers.CacheControl = "no-store";
                http.Response.Headers.XContentTypeOptions = "nosniff";
                http.Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
                return Results.File(content.Bytes!, content.ContentType!, $"certificate.{FileSignature.Extension(content.ContentType!)}");
            })
            .RequireAuthorization();

        // The uploader withdraws an upload that HR has not checked yet; HR (delete privilege for that person) deletes in any state.
        app.MapDelete("/api/absences/{absenceId:guid}/certificates/{documentId:guid}", async (
                Guid absenceId, Guid documentId, ClaimsPrincipal user, IAuthorizationService auth, WorkforceDbContext db, CertificateService service, WorkforceUnitOfWork uow, CancellationToken ct) =>
            {
                if (Endpoints.Me(user) is not { } me)
                {
                    return Results.Forbid();
                }

                if (await db.CertificateDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId && d.AbsenceId == absenceId, ct) is not { } document)
                {
                    return Results.NotFound();
                }

                var mayDelete = await Endpoints.Allows(auth, user, new EmployeeResource(document.EmployeeId), CertificatePrivileges.Delete);
                if (!mayDelete && document.UploadedBy != me)
                {
                    return await Denied(auth, user, document.EmployeeId);
                }

                var result = await uow.RunAsync(() => service.DeleteAsync(documentId, me, mayDelete, ct), r => r.Error == AbsenceError.None, ct);
                return result.Error == AbsenceError.None ? Results.NoContent() : Endpoints.Problem(result.Error);
            })
            .RequireAuthorization();

        // HR: confirm or reject. Without the review privilege for that person the document does not exist for the caller.
        app.MapPost("/api/absences/{absenceId:guid}/certificates/{documentId:guid}/verify", (
                Guid absenceId, Guid documentId, ClaimsPrincipal user, IAuthorizationService auth, WorkforceDbContext db, CertificateService service, WorkforceUnitOfWork uow, CancellationToken ct) =>
                Decide(absenceId, documentId, user, auth, db, uow, (me, token) => service.VerifyAsync(documentId, me, token), ct))
            .RequireAuthorization();

        app.MapPost("/api/absences/{absenceId:guid}/certificates/{documentId:guid}/reject", async (
                Guid absenceId, Guid documentId, RejectCertificateRequest request, ClaimsPrincipal user, IAuthorizationService auth, WorkforceDbContext db,
                CertificateService service, WorkforceUnitOfWork uow, CancellationToken ct) =>
                !Enum.TryParse<CertificateRejectionReason>(request.Reason, ignoreCase: true, out var reason) || reason == CertificateRejectionReason.Unknown || !Enum.IsDefined(reason)
                    ? Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "InvalidReason")
                    : await Decide(absenceId, documentId, user, auth, db, uow, (me, token) => service.RejectAsync(documentId, me, reason, token), ct))
            .RequireAuthorization();

        // HR overview: which sick notes still lack a certificate, which are waiting for the check. Filterable; only people in the caller's reach.
        app.MapGet("/api/sick-notes", async (
                string? certificateStatus, DateOnly? from, DateOnly? to, Guid? storeId, Guid? employeeId, int? page, int? pageSize,
                ClaimsPrincipal user, IPermissionCache cache, WorkforceDbContext db, CancellationToken ct) =>
            {
                if (Endpoints.Me(user) is not { } me || !cache.TryGet(me, out var snapshot) || snapshot is null
                    || !snapshot.Privileges.TryGetValue(CertificatePrivileges.ReadStatus, out var reach))
                {
                    return Results.Forbid();
                }

                var statuses = new List<CertificateStatus>();
                foreach (var name in (certificateStatus ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (!Enum.TryParse<CertificateStatus>(name, ignoreCase: true, out var status) || status == CertificateStatus.Unknown || !Enum.IsDefined(status))
                    {
                        return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "InvalidStatus");
                    }

                    statuses.Add(status);
                }

                var size = Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize);
                var number = Math.Max(page ?? 1, 1);
                var query = from a in db.Absences.AsNoTracking()
                            where a.Type == AbsenceType.Sickness && a.Status == AbsenceStatus.Reported
                            join p in db.People.AsNoTracking() on a.EmployeeId equals p.EmployeeId
                            select new { Absence = a, Person = p };
                if (!reach.Unrestricted)
                {
                    var employees = reach.EmployeeIds.ToList();
                    var stores = reach.StoreIds.ToList();
                    query = query.Where(x => employees.Contains(x.Absence.EmployeeId) || (x.Person.HomeStoreId != null && stores.Contains(x.Person.HomeStoreId.Value)));
                }

                if (statuses.Count > 0)
                {
                    query = query.Where(x => statuses.Contains(x.Absence.CertificateStatus));
                }

                if (from is { } start)
                {
                    query = query.Where(x => x.Absence.To == null || x.Absence.To >= start);
                }

                if (to is { } end)
                {
                    query = query.Where(x => x.Absence.From <= end);
                }

                if (storeId is { } store)
                {
                    query = query.Where(x => x.Person.HomeStoreId == store);
                }

                if (employeeId is { } employee)
                {
                    query = query.Where(x => x.Absence.EmployeeId == employee);
                }

                var total = await query.CountAsync(ct);
                var rows = await query.OrderByDescending(x => x.Absence.From).ThenBy(x => x.Person.Name).Skip((number - 1) * size).Take(size).ToListAsync(ct);
                return Results.Ok(new PageDto<SickNoteOverviewDto>(
                    [.. rows.Select(x => new SickNoteOverviewDto(
                        x.Absence.Id, x.Absence.EmployeeId, x.Person.Name, x.Person.HomeStoreId, x.Absence.From, x.Absence.To, x.Absence.CertificateStatus.ToString(),
                        x.Absence.CertifiedThrough, x.Absence.RemindersSent, x.Absence.LastReminderAt))],
                    total, number, size));
            })
            .RequireAuthorization(AppPolicies.Privilege(CertificatePrivileges.ReadStatus));

        // How long the files are kept after the end of the sick note: HR sets it, the default from the configuration applies until then.
        app.MapGet("/api/certificates/retention", async (CertificateService service, CancellationToken ct) =>
            {
                var retention = await service.GetRetentionAsync(ct);
                return Results.Ok(new CertificateRetentionDto(retention.Days, retention.IsDefault, retention.ChangedAt, retention.ChangedBy));
            })
            .RequireAuthorization(AppPolicies.Privilege(CertificatePrivileges.ManageRetention));

        app.MapPut("/api/certificates/retention", async (
                SetCertificateRetentionRequest request, ClaimsPrincipal user, CertificateService service, WorkforceUnitOfWork uow, CancellationToken ct) =>
            {
                if (Endpoints.Me(user) is not { } me)
                {
                    return Results.Forbid();
                }

                var error = await uow.RunAsync(() => service.SetRetentionAsync(request.RetentionAfterEndDays, me, ct), e => e == AbsenceError.None, ct);
                if (error != AbsenceError.None)
                {
                    return Endpoints.Problem(error);
                }

                var retention = await service.GetRetentionAsync(ct);
                return Results.Ok(new CertificateRetentionDto(retention.Days, retention.IsDefault, retention.ChangedAt, retention.ChangedBy));
            })
            .RequireAuthorization(AppPolicies.Privilege(CertificatePrivileges.ManageRetention));

        // Oversight: who uploaded, opened or decided what and when. Never the content.
        app.MapGet("/api/certificates/access-log", async (
                Guid? absenceId, Guid? employeeId, DateTimeOffset? from, DateTimeOffset? to, int? page, int? pageSize, WorkforceDbContext db, CancellationToken ct) =>
            {
                var size = Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize);
                var number = Math.Max(page ?? 1, 1);
                var query = db.CertificateAccessLog.AsNoTracking().AsQueryable();
                if (absenceId is { } absence)
                {
                    query = query.Where(e => e.AbsenceId == absence);
                }

                if (employeeId is { } employee)
                {
                    query = query.Where(e => e.EmployeeId == employee);
                }

                if (from is { } start)
                {
                    query = query.Where(e => e.At >= start);
                }

                if (to is { } end)
                {
                    query = query.Where(e => e.At <= end);
                }

                var total = await query.CountAsync(ct);
                var rows = await query.OrderByDescending(e => e.At).Skip((number - 1) * size).Take(size).ToListAsync(ct);
                return Results.Ok(new PageDto<CertificateAccessEntryDto>(
                    [.. rows.Select(e => new CertificateAccessEntryDto(e.Id, e.At, e.ActorId, e.Action.ToString(), e.AbsenceId, e.DocumentId, e.EmployeeId))],
                    total, number, size));
            })
            .RequireAuthorization(AppPolicies.Privilege(CertificatePrivileges.ReadAccessLog));

        return app;
    }

    private static async Task<IResult> Decide(
        Guid absenceId, Guid documentId, ClaimsPrincipal user, IAuthorizationService auth, WorkforceDbContext db, WorkforceUnitOfWork uow,
        Func<Guid, CancellationToken, Task<CertificateResult>> decision, CancellationToken ct)
    {
        if (Endpoints.Me(user) is not { } me)
        {
            return Results.Forbid();
        }

        if (await db.CertificateDocuments.AsNoTracking().Where(d => d.Id == documentId && d.AbsenceId == absenceId).Select(d => (Guid?)d.EmployeeId).FirstOrDefaultAsync(ct) is not { } owner)
        {
            return Results.NotFound();
        }

        if (!await Endpoints.Allows(auth, user, new EmployeeResource(owner), CertificatePrivileges.Review))
        {
            return await Denied(auth, user, owner);
        }

        var result = await uow.RunAsync(() => decision(me, ct), r => r.Error == AbsenceError.None, ct);
        return result.Error == AbsenceError.None ? Results.Ok(ToDto(result.Document!, me, actorMayReview: true)) : Endpoints.Problem(result.Error);
    }

    /// <summary>
    /// The answer to somebody who must not open, check or delete a document: "permission denied" if they may see that a certificate exists
    /// (the status privilege for that person), otherwise "not found" so that its existence stays hidden.
    /// </summary>
    private static async Task<IResult> Denied(IAuthorizationService auth, ClaimsPrincipal user, Guid employeeId) =>
        await Endpoints.Allows(auth, user, new EmployeeResource(employeeId), CertificatePrivileges.ReadStatus) ? Results.Forbid() : Results.NotFound();

    /// <summary>
    /// Reads the form (file plus the first and last day from the certificate). The file stays in memory (no temporary file with a health
    /// document on disk, see the form options in Program) and its size is limited before it is read.
    /// </summary>
    private static async Task<IResult> UploadAsync(
        HttpRequest request, Guid absenceId, Guid uploader, CertificateService service, WorkforceUnitOfWork uow, int maxBytes, CancellationToken ct)
    {
        if (!request.HasFormContentType)
        {
            return Endpoints.Problem(AbsenceError.UnsupportedFileType);
        }

        if (request.ContentLength is { } length && length > maxBytes + 64 * 1024)
        {
            return Endpoints.Problem(AbsenceError.FileTooLarge);
        }

        if (request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
        {
            limit.MaxRequestBodySize = maxBytes + 64 * 1024;
        }

        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync(ct);
        }
        catch (Exception ex) when (ex is InvalidDataException or BadHttpRequestException)
        {
            return Endpoints.Problem(AbsenceError.FileTooLarge);
        }

        if (form.Files.Count != 1 || form.Files[0].Length == 0
            || !TryDate(form["certifiedFrom"], out var from) || !TryDate(form["certifiedTo"], out var to))
        {
            return Endpoints.Problem(AbsenceError.InvalidPeriod);
        }

        var file = form.Files[0];
        if (file.Length > maxBytes)
        {
            return Endpoints.Problem(AbsenceError.FileTooLarge);
        }

        var bytes = new byte[file.Length];
        await using (var stream = file.OpenReadStream())
        {
            await stream.ReadExactlyAsync(bytes, ct);
        }

        var result = await uow.RunAsync(() => service.UploadAsync(absenceId, uploader, bytes, from, to, ct), r => r.Error == AbsenceError.None, ct);
        return result.Error == AbsenceError.None ? Results.Ok(ToDto(result.Document!, uploader, actorMayReview: false)) : Endpoints.Problem(result.Error);
    }

    private static bool TryDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    /// <summary>The entry as the caller may see it: who uploaded it only for the uploader and reviewers, and whether they can open the file.</summary>
    private static CertificateDto ToDto(CertificateDocument d, Guid actorId, bool actorMayReview)
    {
        var mayOpen = actorMayReview || d.UploadedBy == actorId;
        return new(
            d.Id, d.AbsenceId, d.State.ToString(), d.CertifiedFrom, d.CertifiedTo, d.UploadedAt, mayOpen ? d.UploadedBy : null, d.ContentType, d.Length,
            d.RejectionReason?.ToString(), d.HasContent, CanOpen: mayOpen && d.HasContent);
    }
}
