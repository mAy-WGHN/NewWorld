using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using Portal.Api.Http;
using Portal.Core;
using Portal.Core.Faqs;
using Portal.Infrastructure;
using Portal.Infrastructure.Faqs;
using Shared.Authorization;

namespace Portal.Api.Faqs;

/// <summary>
/// A FAQ entry. <see cref="Content"/> is the answer as a rich-text document (TipTap JSON, see <c>FaqContent</c>); <see cref="Description"/> is the same answer as
/// Markdown, derived from it, for searching and for old clients. <see cref="Attachments"/> (files attached to the entry) are only filled for signed-in callers; the entry itself is public.
/// </summary>
public sealed record FaqDto(
    Guid Id, string Title, string Description, int Order, bool Archived, DateTimeOffset CreatedAt, IReadOnlyList<FaqAttachmentDto>? Attachments = null, JsonNode? Content = null);

/// <summary>An attached file; <see cref="Url"/> is the path to fetch it (signed-in callers only).</summary>
public sealed record FaqAttachmentDto(Guid Id, string FileName, string ContentType, long Length, bool IsImage, string Url);

/// <summary>The answer comes as <c>content</c> (a document); old clients may send only <c>description</c> (a text without formatting).</summary>
public sealed record CreateFaqRequest(string? Title, string? Description, JsonNode? Content = null);

public sealed record UpdateFaqRequest(string? Title, string? Description, bool? Archived, JsonNode? Content = null);

public sealed record ReorderFaqRequest(List<Guid>? OrderedIds);

public sealed record FaqImageUploadDto(Guid Token, string Url);

/// <summary>Status: orphaned = no entry, archived = only archived entries, active = an active entry links it.</summary>
public sealed record FaqImageDto(Guid Id, string Url, DateTimeOffset CreatedAt, string Status, Guid? FaqId, string? FaqTitle);

/// <summary>
/// FAQ with Markdown answers and images. Reading the active entries and viewing an image is public (the random image id is the only access control);
/// everything else needs a privilege.
/// </summary>
public static class FaqEndpoints
{
    private const string ImageNotFound = "Bild wurde nicht gefunden.";
    private const string AttachmentNotFound = "Die Datei wurde nicht gefunden.";

    public static WebApplication MapFaqEndpoints(this WebApplication app)
    {
        app.MapGet("/api/faqs", async (ClaimsPrincipal user, PortalDbContext db, CancellationToken ct) => Answer.Ok(await ActiveAsync(db, user.Identity?.IsAuthenticated == true, ct))).AllowAnonymous();

        app.MapGet("/api/faqs/archived", async (PortalDbContext db, CancellationToken ct) =>
                Answer.Ok(await WithAttachmentsAsync(db, await db.Faqs.AsNoTracking().Where(f => f.Archived).OrderBy(f => f.SortOrder).ThenBy(f => f.Id).ToListAsync(ct), true, ct)))
            .RequireAuthorization(AppPolicies.Privilege(PortalPrivileges.FaqRead));

        app.MapPost("/api/faqs", async (CreateFaqRequest request, PortalDbContext db, TimeProvider time, CancellationToken ct) =>
            {
                var validation = new Validator();
                validation.RequiredText("title", request.Title, 255);
                var (description, contentJson) = AnswerOf(request.Content, request.Description, validation);
                validation.ThrowIfInvalid();

                // Position = highest + 1 over all entries (also archived ones); 0 for the first.
                var next = await db.Faqs.Select(f => (int?)f.SortOrder).MaxAsync(ct) + 1 ?? 0;
                var faq = new FaqRecord { Title = request.Title!.Trim(), Description = description!, ContentJson = contentJson, SortOrder = next, CreatedAt = time.GetUtcNow() };
                db.Faqs.Add(faq);
                await db.SaveChangesAsync(ct); // the entry first, so the images can point to it
                await SyncImageLinksAsync(db, faq, ct);
                await db.SaveChangesAsync(ct);
                return Answer.Created(ToDto(faq));
            })
            .RequireAuthorization(AppPolicies.Privilege(PortalPrivileges.FaqCreate));

        // Must come before /{id}; the guid constraint keeps them apart.
        app.MapPatch("/api/faqs/reorder", async (ReorderFaqRequest request, PortalDbContext db, CancellationToken ct) =>
            {
                var ids = request.OrderedIds;
                if (ids is not { Count: > 0 })
                {
                    throw ApiException.Validation("orderedIds", "Es muss mindestens ein Eintrag angegeben werden.");
                }

                if (ids.Distinct().Count() != ids.Count)
                {
                    throw ApiException.Validation("orderedIds", "Alle Einträge müssen eindeutig sein.");
                }

                var active = await db.Faqs.Where(f => !f.Archived).ToListAsync(ct);
                if (!active.Select(f => f.Id).ToHashSet().SetEquals(ids))
                {
                    throw ApiException.BadRequest("Die Reihenfolge muss genau die aktiven FAQ-Einträge enthalten.");
                }

                var byId = active.ToDictionary(f => f.Id);
                for (var position = 0; position < ids.Count; position++)
                {
                    byId[ids[position]].SortOrder = position;
                }

                await db.SaveChangesAsync(ct);
                return Answer.Ok(await ActiveAsync(db, true, ct));
            })
            .RequireAuthorization(AppPolicies.Privilege(PortalPrivileges.FaqUpdate));

        app.MapPatch("/api/faqs/{id:guid}", async (Guid id, UpdateFaqRequest request, PortalDbContext db, CancellationToken ct) =>
            {
                var validation = new Validator();
                validation.MaxLength("title", request.Title, 255);
                var answered = request.Content is not null || request.Description is not null;
                var (newDescription, newContentJson) = answered ? AnswerOf(request.Content, request.Description, validation) : (null, null);
                validation.ThrowIfInvalid();

                var faq = await db.Faqs.FindAsync([id], ct) ?? throw ApiException.NotFound();
                if (request.Title is not null)
                {
                    faq.Title = request.Title.Trim();
                }

                if (request.Archived is { } archived)
                {
                    faq.Archived = archived;
                }

                if (answered && (newContentJson is not null || newDescription != faq.Description))
                {
                    // A document replaces the answer; a plain text from an old client replaces it, too, and the document is derived from it again.
                    faq.Description = newDescription!;
                    faq.ContentJson = newContentJson;
                    await SyncImageLinksAsync(db, faq, ct);
                }

                await db.SaveChangesAsync(ct);
                return Answer.Ok(ToDto(faq, await AttachmentsOfAsync(db, faq.Id, ct)));
            })
            .RequireAuthorization(AppPolicies.Privilege(PortalPrivileges.FaqUpdate));

        // Hard delete; linked images become orphaned (foreign key SET NULL), attached files are deleted with the entry.
        app.MapDelete("/api/faqs/{id:guid}", async (Guid id, PortalDbContext db, IFaqAttachmentStorage attachmentStorage, CancellationToken ct) =>
            {
                var faq = await db.Faqs.FindAsync([id], ct) ?? throw ApiException.NotFound();
                var files = await db.FaqAttachments.AsNoTracking().Where(a => a.FaqId == id).Select(a => new { a.Id, a.Extension }).ToListAsync(ct);
                db.Faqs.Remove(faq);
                await db.SaveChangesAsync(ct);
                foreach (var file in files)
                {
                    attachmentStorage.Delete(file.Id, file.Extension);
                }

                return Answer.NoContent();
            })
            .RequireAuthorization(AppPolicies.Privilege(PortalPrivileges.FaqDelete));

        MapImages(app);
        MapAttachments(app);
        return app;
    }

    /// <summary>
    /// Files attached to an entry (PDF, images, Office documents). Attaching and removing needs faq:update. Fetching needs the sign-in (the entry
    /// itself is public, its files are not); a file of an archived entry only with faq:read.
    /// </summary>
    private static void MapAttachments(WebApplication app)
    {
        app.MapPost("/api/faqs/{id:guid}/attachments", async (Guid id, HttpRequest request, PortalDbContext db, IFaqAttachmentStorage storage, TimeProvider time, CancellationToken ct) =>
            {
                if (!await db.Faqs.AnyAsync(f => f.Id == id, ct))
                {
                    throw ApiException.NotFound();
                }

                if (!request.HasFormContentType || (await request.ReadFormAsync(ct)).Files.GetFile("file") is not { } file)
                {
                    throw ApiException.BadRequest("Es wurde keine Datei übermittelt.");
                }

                if (file.Length == 0 || file.Length > FaqAttachmentFormat.MaxBytes)
                {
                    throw ApiException.Validation("file", "Die Datei ist leer oder zu groß. Erlaubt sind höchstens 10 MB.");
                }

                if (await db.FaqAttachments.CountAsync(a => a.FaqId == id, ct) >= FaqAttachmentFormat.MaxPerEntry)
                {
                    throw ApiException.Conflict($"An einen Eintrag können höchstens {FaqAttachmentFormat.MaxPerEntry} Dateien angeheftet werden.");
                }

                await using var content = new MemoryStream((int)file.Length);
                await using (var upload = file.OpenReadStream())
                {
                    await upload.CopyToAsync(content, ct);
                }

                var header = content.GetBuffer().AsSpan(0, (int)Math.Min(content.Length, FaqAttachmentFormat.HeaderLength));
                var kind = FaqAttachmentFormat.Detect(header, file.FileName, content)
                    ?? throw ApiException.Validation("file", "Erlaubt sind PDF, Bilder (PNG, JPEG, WebP) und Office-Dokumente (Word, Excel, PowerPoint).");
                var record = new FaqAttachmentRecord
                {
                    FaqId = id,
                    FileName = FaqAttachmentFormat.CleanName(file.FileName, kind.Extension),
                    Extension = kind.Extension,
                    Length = content.Length,
                    CreatedAt = time.GetUtcNow(),
                };
                content.Position = 0;
                await storage.SaveAsync(record.Id, kind.Extension, content, ct);
                db.FaqAttachments.Add(record);
                await db.SaveChangesAsync(ct);
                return Answer.Created(AttachmentDto(record));
            })
            .RequireAuthorization(AppPolicies.Privilege(PortalPrivileges.FaqUpdate));

        app.MapGet("/api/faqs/{id:guid}/attachments/{attachmentId:guid}", async (
                Guid id, Guid attachmentId, ClaimsPrincipal user, IAuthorizationService authorization, HttpContext http, PortalDbContext db, IFaqAttachmentStorage storage, CancellationToken ct) =>
            {
                var attachment = await db.FaqAttachments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == attachmentId && a.FaqId == id, ct) ?? throw ApiException.NotFound(AttachmentNotFound);
                var archived = await db.Faqs.AsNoTracking().Where(f => f.Id == id).Select(f => f.Archived).FirstOrDefaultAsync(ct);
                if (archived && !(await authorization.AuthorizeAsync(user, AppPolicies.Privilege(PortalPrivileges.FaqRead))).Succeeded)
                {
                    throw ApiException.NotFound(AttachmentNotFound);
                }

                var stream = storage.Open(attachment.Id, attachment.Extension) ?? throw ApiException.NotFound(AttachmentNotFound);
                var contentType = FaqAttachmentFormat.ContentType(attachment.Extension);
                // Never interpreted by the browser; images and PDF open in the tab, Office documents are downloaded.
                http.Response.Headers.XContentTypeOptions = "nosniff";
                http.Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
                http.Response.Headers.CacheControl = "private, no-store";
                var inline = FaqAttachmentFormat.IsImage(attachment.Extension) || attachment.Extension == "pdf";
                http.Response.Headers.ContentDisposition = new ContentDispositionHeaderValue(inline ? "inline" : "attachment") { FileNameStar = attachment.FileName, FileName = attachment.FileName }.ToString();
                return Results.Stream(stream, contentType);
            })
            .RequireAuthorization();

        app.MapDelete("/api/faqs/{id:guid}/attachments/{attachmentId:guid}", async (Guid id, Guid attachmentId, PortalDbContext db, IFaqAttachmentStorage storage, CancellationToken ct) =>
            {
                var attachment = await db.FaqAttachments.FirstOrDefaultAsync(a => a.Id == attachmentId && a.FaqId == id, ct) ?? throw ApiException.NotFound(AttachmentNotFound);
                db.FaqAttachments.Remove(attachment);
                await db.SaveChangesAsync(ct);
                storage.Delete(attachment.Id, attachment.Extension);
                return Answer.NoContent();
            })
            .RequireAuthorization(AppPolicies.Privilege(PortalPrivileges.FaqUpdate));
    }

    private static void MapImages(WebApplication app)
    {
        app.MapPost("/api/admin/faq/images", async (HttpRequest request, PortalDbContext db, IFaqImageStorage storage, TimeProvider time, CancellationToken ct) =>
            {
                if (!request.HasFormContentType || (await request.ReadFormAsync(ct)).Files.GetFile("file") is not { } file)
                {
                    throw ApiException.BadRequest("Es wurde keine Datei übermittelt.");
                }

                if (file.Length > FaqImageFormat.MaxBytes)
                {
                    throw ApiException.Validation("file", "Die Datei ist zu groß. Erlaubt sind höchstens 5 MB.");
                }

                await using var stream = file.OpenReadStream();
                var header = new byte[FaqImageFormat.HeaderLength];
                var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
                var extension = FaqImageFormat.DetectExtension(header.AsSpan(0, read))
                    ?? throw ApiException.Validation("file", "Nur PNG-, JPEG- oder WebP-Bilder sind erlaubt.");
                stream.Position = 0;

                var image = new FaqImageRecord { Extension = extension, CreatedAt = time.GetUtcNow() };
                await storage.SaveAsync(image.Id, extension, stream, ct);
                db.FaqImages.Add(image);
                await db.SaveChangesAsync(ct);
                return Answer.Created(new FaqImageUploadDto(image.Id, PublicUrl(request, image.Id)));
            })
            .RequireAuthorization(AppPolicies.Privilege(PortalPrivileges.FaqCreate));

        // Public: the random id is the only access control.
        app.MapGet("/api/faqs/images/{id:guid}", async (Guid id, PortalDbContext db, IFaqImageStorage storage, CancellationToken ct) =>
            {
                var image = await db.FaqImages.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw ApiException.NotFound(ImageNotFound);
                var stream = storage.Open(image.Id, image.Extension) ?? throw ApiException.NotFound(ImageNotFound);
                return Results.Stream(stream, FaqImageFormat.ContentType(image.Extension));
            })
            .AllowAnonymous();

        app.MapGet("/api/admin/faq/images", async (string? status, HttpRequest request, PortalDbContext db, CancellationToken ct) =>
            {
                if (status is not (null or "orphaned" or "archived" or "active"))
                {
                    throw ApiException.BadRequest("Ungültiger status-Filter — erlaubt sind orphaned, archived, active.");
                }

                var rows = await (
                    from i in db.FaqImages.AsNoTracking()
                    join f in db.Faqs.AsNoTracking() on i.FaqId equals f.Id into joined
                    from f in joined.DefaultIfEmpty()
                    select new { i.Id, i.CreatedAt, i.FaqId, FaqTitle = f == null ? null : f.Title, Status = f == null ? "orphaned" : f.Archived ? "archived" : "active" })
                    .Where(r => status == null || r.Status == status)
                    .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
                    .ToListAsync(ct);
                return Answer.Ok(rows.Select(r => new FaqImageDto(r.Id, PublicUrl(request, r.Id), r.CreatedAt, r.Status, r.FaqId, r.FaqTitle)).ToList());
            })
            .RequireAuthorization(AppPolicies.Privilege(PortalPrivileges.FaqRead));

        app.MapDelete("/api/admin/faq/images/{id:guid}", async (Guid id, PortalDbContext db, IFaqImageStorage storage, CancellationToken ct) =>
            {
                var image = await db.FaqImages.FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw ApiException.NotFound();
                if (image.FaqId is { } faqId && await db.Faqs.AnyAsync(f => f.Id == faqId && !f.Archived, ct))
                {
                    throw ApiException.Conflict("Das Bild wird noch in einer aktiven FAQ-Antwort verwendet.");
                }

                db.FaqImages.Remove(image);
                await db.SaveChangesAsync(ct);
                storage.Delete(image.Id, image.Extension);
                return Answer.NoContent();
            })
            .RequireAuthorization(AppPolicies.Privilege(PortalPrivileges.FaqDelete));
    }

    private static async Task<List<FaqDto>> ActiveAsync(PortalDbContext db, bool withAttachments, CancellationToken ct) =>
        await WithAttachmentsAsync(db, await db.Faqs.AsNoTracking().Where(f => !f.Archived).OrderBy(f => f.SortOrder).ThenBy(f => f.Id).ToListAsync(ct), withAttachments, ct);

    private static FaqDto ToDto(FaqRecord f, IReadOnlyList<FaqAttachmentDto>? attachments = null) =>
        new(f.Id, f.Title, f.Description, f.SortOrder, f.Archived, f.CreatedAt, attachments ?? [], ContentOf(f));

    /// <summary>The document of an entry; an entry that only has the text of an old client gets a document made of its paragraphs.</summary>
    private static JsonNode ContentOf(FaqRecord f) => f.ContentJson is { } json && JsonNode.Parse(json) is { } document ? document : FaqContent.FromPlainText(f.Description);

    /// <summary>
    /// The answer of a request: the cleaned document with its Markdown (new clients), or the text as it is (old clients that only know
    /// <c>description</c>). What is wrong with it is added to <paramref name="validation"/>.
    /// </summary>
    private static (string? Description, string? ContentJson) AnswerOf(JsonNode? content, string? description, Validator validation)
    {
        if (content is not null)
        {
            if (FaqContent.TryClean(content, out var error) is { } clean)
            {
                return (FaqContent.ToMarkdown(clean), clean.ToJsonString());
            }

            validation.Add("content", error!);
            return (null, null);
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            validation.Add("content", "Bitte gib eine Antwort ein.");
        }

        return (description, null);
    }

    private static FaqAttachmentDto AttachmentDto(FaqAttachmentRecord a) =>
        new(a.Id, a.FileName, FaqAttachmentFormat.ContentType(a.Extension), a.Length, FaqAttachmentFormat.IsImage(a.Extension), $"/api/faqs/{a.FaqId}/attachments/{a.Id}");

    private static async Task<List<FaqAttachmentDto>> AttachmentsOfAsync(PortalDbContext db, Guid faqId, CancellationToken ct) =>
        (await db.FaqAttachments.AsNoTracking().Where(a => a.FaqId == faqId).OrderBy(a => a.CreatedAt).ThenBy(a => a.Id).ToListAsync(ct)).Select(AttachmentDto).ToList();

    /// <summary>The entries with their attached files (one query for all), or without them for callers who are not signed in.</summary>
    private static async Task<List<FaqDto>> WithAttachmentsAsync(PortalDbContext db, List<FaqRecord> faqs, bool include, CancellationToken ct)
    {
        var byFaq = new Dictionary<Guid, List<FaqAttachmentDto>>();
        if (include && faqs.Count > 0)
        {
            var ids = faqs.Select(f => f.Id).ToList();
            var rows = await db.FaqAttachments.AsNoTracking().Where(a => ids.Contains(a.FaqId)).OrderBy(a => a.CreatedAt).ThenBy(a => a.Id).ToListAsync(ct);
            byFaq = rows.GroupBy(a => a.FaqId).ToDictionary(g => g.Key, g => g.Select(AttachmentDto).ToList());
        }

        return [.. faqs.Select(f => ToDto(f, byFaq.GetValueOrDefault(f.Id)))];
    }

    /// <summary>The entry links the images its Markdown refers to (the one saved last wins) and releases those no longer mentioned.</summary>
    private static async Task SyncImageLinksAsync(PortalDbContext db, FaqRecord faq, CancellationToken ct)
    {
        var referenced = FaqImageFormat.ReferencedImages(faq.Description);
        var images = await db.FaqImages.Where(i => referenced.Contains(i.Id) || i.FaqId == faq.Id).ToListAsync(ct);
        foreach (var image in images)
        {
            image.FaqId = referenced.Contains(image.Id) ? faq.Id : null;
        }
    }

    // Absolute, because the frontend runs on another origin (scheme and host from the forwarded headers).
    private static string PublicUrl(HttpRequest request, Guid id) => $"{request.Scheme}://{request.Host}/api/faqs/images/{id}";
}
