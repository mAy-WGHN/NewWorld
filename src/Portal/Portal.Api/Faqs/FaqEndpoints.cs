using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Portal.Api.Http;
using Portal.Core;
using Portal.Core.Faqs;
using Portal.Infrastructure;
using Portal.Infrastructure.Faqs;
using Shared.Authorization;

namespace Portal.Api.Faqs;

public sealed record FaqDto(Guid Id, string Title, string Description, int Order, bool Archived, DateTimeOffset CreatedAt);

public sealed record CreateFaqRequest(string? Title, string? Description);

public sealed record UpdateFaqRequest(string? Title, string? Description, bool? Archived);

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

    public static WebApplication MapFaqEndpoints(this WebApplication app)
    {
        app.MapGet("/api/faqs", async (PortalDbContext db, CancellationToken ct) => Answer.Ok(await ActiveAsync(db, ct))).AllowAnonymous();

        app.MapGet("/api/faqs/archived", async (PortalDbContext db, CancellationToken ct) =>
                Answer.Ok(await db.Faqs.AsNoTracking().Where(f => f.Archived).OrderBy(f => f.SortOrder).ThenBy(f => f.Id).Select(f => ToDto(f)).ToListAsync(ct)))
            .RequireAuthorization(AppPolicies.Privilege(PortalPrivileges.FaqRead));

        app.MapPost("/api/faqs", async (CreateFaqRequest request, PortalDbContext db, TimeProvider time, CancellationToken ct) =>
            {
                var validation = new Validator();
                validation.RequiredText("title", request.Title, 255);
                validation.RequiredText("description", request.Description);
                validation.ThrowIfInvalid();

                // Position = highest + 1 over all entries (also archived ones); 0 for the first.
                var next = await db.Faqs.Select(f => (int?)f.SortOrder).MaxAsync(ct) + 1 ?? 0;
                var faq = new FaqRecord { Title = request.Title!.Trim(), Description = request.Description!, SortOrder = next, CreatedAt = time.GetUtcNow() };
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
                return Answer.Ok(await ActiveAsync(db, ct));
            })
            .RequireAuthorization(AppPolicies.Privilege(PortalPrivileges.FaqUpdate));

        app.MapPatch("/api/faqs/{id:guid}", async (Guid id, UpdateFaqRequest request, PortalDbContext db, CancellationToken ct) =>
            {
                var validation = new Validator();
                validation.MaxLength("title", request.Title, 255);
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

                if (request.Description is not null && request.Description != faq.Description)
                {
                    faq.Description = request.Description;
                    await SyncImageLinksAsync(db, faq, ct);
                }

                await db.SaveChangesAsync(ct);
                return Answer.Ok(ToDto(faq));
            })
            .RequireAuthorization(AppPolicies.Privilege(PortalPrivileges.FaqUpdate));

        // Hard delete; linked images become orphaned (foreign key SET NULL).
        app.MapDelete("/api/faqs/{id:guid}", async (Guid id, PortalDbContext db, CancellationToken ct) =>
            {
                var faq = await db.Faqs.FindAsync([id], ct) ?? throw ApiException.NotFound();
                db.Faqs.Remove(faq);
                await db.SaveChangesAsync(ct);
                return Answer.NoContent();
            })
            .RequireAuthorization(AppPolicies.Privilege(PortalPrivileges.FaqDelete));

        MapImages(app);
        return app;
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

    private static async Task<List<FaqDto>> ActiveAsync(PortalDbContext db, CancellationToken ct) =>
        await db.Faqs.AsNoTracking().Where(f => !f.Archived).OrderBy(f => f.SortOrder).ThenBy(f => f.Id).Select(f => ToDto(f)).ToListAsync(ct);

    private static FaqDto ToDto(FaqRecord f) => new(f.Id, f.Title, f.Description, f.SortOrder, f.Archived, f.CreatedAt);

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
