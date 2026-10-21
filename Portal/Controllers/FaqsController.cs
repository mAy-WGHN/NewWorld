using Portal.Authentication;
using System.Text.RegularExpressions;
using Portal.Common.Api;
using Portal.Contracts;
using Portal.Data;
using Portal.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Portal.Controllers;

[Route("api/faqs")]
public partial class FaqsController(IrsDbContext db) : ApiControllerBase
{
    [GeneratedRegex("/api/faqs/images/([0-9a-fA-F-]{36})")]
    private static partial Regex ImageUrlRegex();

    [HttpGet, AllowAnonymous]
    public async Task<IActionResult> List(CancellationToken ct) => Success(await ActiveAsync(ct));

    [HttpGet("archived"), HasPrivilege<Faq>(CrudAction.Read)]
    public async Task<IActionResult> Archived(CancellationToken ct) =>
        Success(await db.Faqs.Where(f => f.Archived).OrderBy(f => f.SortOrder).ThenBy(f => f.Id)
            .Select(FaqResponse.Projection).ToListAsync(ct));

    [HttpPost, HasPrivilege<Faq>(CrudAction.Create)]
    public async Task<IActionResult> Create(CreateFaqRequest request, CancellationToken ct)
    {
        // Position = MAX + 1 über alle FAQs (auch archivierte); bei leerer Tabelle 0.
        var next = await db.Faqs.Select(f => (int?)f.SortOrder).MaxAsync(ct) + 1 ?? 0;
        var faq = new Faq { Title = request.Title!.Trim(), Description = request.Description!, SortOrder = next };
        db.Faqs.Add(faq);

        await SyncImageLinksAsync(faq, ct);
        await db.SaveChangesAsync(ct);
        return CreatedData(FaqResponse.From(faq));
    }

    // Muss vor /{id} aufgelöst werden; {id:guid} verhindert die Kollision.
    [HttpPatch("reorder"), HasPrivilege<Faq>(CrudAction.Update)]
    public async Task<IActionResult> Reorder(ReorderFaqRequest request, CancellationToken ct)
    {
        var ids = request.OrderedIds!;
        if (ids.Distinct().Count() != ids.Count)
            throw ApiException.Validation("orderedIds", "Alle Einträge müssen eindeutig sein.");

        var active = await db.Faqs.Where(f => !f.Archived).ToListAsync(ct);
        if (!active.Select(f => f.Id).ToHashSet().SetEquals(ids))
            throw ApiException.BadRequest("Die Reihenfolge muss genau die aktiven FAQ-Einträge enthalten.");

        var byId = active.ToDictionary(f => f.Id);
        for (var position = 0; position < ids.Count; position++) byId[ids[position]].SortOrder = position;

        await db.SaveChangesAsync(ct);
        return Success(await ActiveAsync(ct));
    }

    [HttpPatch("{id:guid}"), HasPrivilege<Faq>(CrudAction.Update)]
    public async Task<IActionResult> Update(Guid id, UpdateFaqRequest request, CancellationToken ct)
    {
        var faq = await db.Faqs.FindAsync([id], ct) ?? throw ApiException.NotFound();

        if (request.Title is not null) faq.Title = request.Title.Trim();
        if (request.Archived is not null) faq.Archived = request.Archived.Value;
        if (request.Description is not null && request.Description != faq.Description)
        {
            faq.Description = request.Description;
            await SyncImageLinksAsync(faq, ct);
        }

        await db.SaveChangesAsync(ct);
        return Success(FaqResponse.From(faq));
    }

    // Hartes Löschen; verknüpfte Bilder werden per ON DELETE SET NULL verwaist.
    [HttpDelete("{id:guid}"), HasPrivilege<Faq>(CrudAction.Delete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var faq = await db.Faqs.FindAsync([id], ct) ?? throw ApiException.NotFound();
        db.Faqs.Remove(faq);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private Task<List<FaqResponse>> ActiveAsync(CancellationToken ct) =>
        db.Faqs.Where(f => !f.Archived).OrderBy(f => f.SortOrder).ThenBy(f => f.Id)
            .Select(FaqResponse.Projection).ToListAsync(ct);

    /// <summary>
    /// Verknüpft die im Markdown referenzierten Bilder mit dieser FAQ (die zuletzt speichernde FAQ gewinnt)
    /// und gibt Bilder frei, die nicht mehr vorkommen (→ verwaist). Nur Regex, kein Markdown-Parser.
    /// </summary>
    private async Task SyncImageLinksAsync(Faq faq, CancellationToken ct)
    {
        var referenced = ImageUrlRegex().Matches(faq.Description)
            .Select(m => Guid.TryParse(m.Groups[1].Value, out var id) ? id : (Guid?)null)
            .OfType<Guid>().ToHashSet();

        var images = await db.FaqImages
            .Where(i => referenced.Contains(i.Id) || i.FaqId == faq.Id)
            .ToListAsync(ct);

        foreach (var image in images)
            image.FaqId = referenced.Contains(image.Id) ? faq.Id : null;
    }
}
