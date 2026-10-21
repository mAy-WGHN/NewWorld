using Portal.Authentication;
using Portal.Common.Api;
using Portal.Contracts;
using Portal.Data;
using Portal.Domain.Entities;
using Portal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Portal.Controllers;

public class FaqImagesController(IrsDbContext db, FaqImageStorage storage) : ApiControllerBase
{
    private const string ImageNotFound = "Bild wurde nicht gefunden.";
    private const string UnsupportedType = "Nur PNG-, JPEG- oder WebP-Bilder sind erlaubt.";

    [HttpPost("api/admin/faq/images"), HasPrivilege<Faq>(CrudAction.Create)]
    public async Task<IActionResult> Upload(IFormFile? file, CancellationToken ct)
    {
        if (file is null) throw ApiException.BadRequest("Es wurde keine Datei übermittelt.");
        if (file.Length > FaqImageStorage.MaxBytes)
            throw ApiException.Validation("file", "Die Datei ist zu groß. Erlaubt sind höchstens 5 MB.");

        await using var stream = file.OpenReadStream();
        var header = new byte[12];
        var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
        var extension = FaqImageStorage.DetectExtension(header.AsSpan(0, read))
                        ?? throw ApiException.Validation("file", UnsupportedType);
        stream.Position = 0;

        var image = new FaqImage { Extension = extension };
        await storage.SaveAsync(image.Id, extension, stream, ct);
        db.FaqImages.Add(image);
        await db.SaveChangesAsync(ct);

        return CreatedData(new FaqImageUploadResponse(image.Id, PublicUrl(image.Id)));
    }

    // Öffentlich: die unratbare UUIDv4 ist die einzige Zugriffskontrolle.
    [HttpGet("api/faqs/images/{id:guid}"), AllowAnonymous]
    public async Task<IActionResult> View(Guid id, CancellationToken ct)
    {
        var image = await db.FaqImages.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct)
                    ?? throw ApiException.NotFound(ImageNotFound);

        var path = storage.PathFor(image.Id, image.Extension);
        if (!System.IO.File.Exists(path)) throw ApiException.NotFound(ImageNotFound);
        return PhysicalFile(path, FaqImageStorage.ContentType(image.Extension));
    }

    [HttpGet("api/admin/faq/images"), HasPrivilege<Faq>(CrudAction.Read)]
    public async Task<IActionResult> List([FromQuery] string? status, CancellationToken ct)
    {
        if (status is not (null or "orphaned" or "archived" or "active"))
            throw ApiException.BadRequest("Ungültiger status-Filter — erlaubt sind orphaned, archived, active.");

        var query = db.FaqImages.AsNoTracking().Select(i => new
        {
            i.Id, i.CreatedAt, i.FaqId, FaqTitle = i.Faq == null ? null : i.Faq.Title,
            Status = i.Faq == null ? "orphaned" : i.Faq.Archived ? "archived" : "active"
        });
        if (status is not null) query = query.Where(i => i.Status == status);

        var rows = await query.OrderByDescending(i => i.CreatedAt).ThenByDescending(i => i.Id).ToListAsync(ct);
        return Success(rows.Select(i =>
            new FaqImageResponse(i.Id, PublicUrl(i.Id), i.CreatedAt, i.Status, i.FaqId, i.FaqTitle)).ToList());
    }

    [HttpDelete("api/admin/faq/images/{id:guid}"), HasPrivilege<Faq>(CrudAction.Delete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var image = await db.FaqImages.Include(i => i.Faq).FirstOrDefaultAsync(i => i.Id == id, ct)
                    ?? throw ApiException.NotFound();

        if (image.Faq is { Archived: false })
            throw ApiException.Conflict("Das Bild wird noch in einer aktiven FAQ-Antwort verwendet.");

        storage.Delete(image.Id, image.Extension);
        db.FaqImages.Remove(image);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // Absolut, weil das Frontend auf einer anderen Origin läuft (Schema/Host über ForwardedHeaders).
    private string PublicUrl(Guid id) => $"{Request.Scheme}://{Request.Host}/api/faqs/images/{id}";
}
