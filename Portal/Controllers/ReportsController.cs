using Portal.Authentication;
using Portal.Common.Api;
using Portal.Common.RateLimiting;
using Portal.Contracts;
using Portal.Data;
using Portal.Domain.Entities;
using Portal.Domain.Enums;
using Portal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Portal.Controllers;

[Route("api/reports")]
public class ReportsController(IrsDbContext db, ReportQueryService queries, IReportEvents events, ILocationDirectory locationDirectory, ScopeContext scope) : ApiControllerBase
{
    private const string LocationsNotFound = "Eine oder mehrere Filialen wurden nicht gefunden oder sind archiviert.";

    [HttpGet, HasPrivilege<Report>(CrudAction.Read)]
    public async Task<IActionResult> List([FromQuery] ReportFilterRequest filter, CancellationToken ct) =>
        Success(await queries.ListAsync(filter, forceArchived: null, ct));

    [HttpGet("{id:guid}"), HasPrivilege<Report>(CrudAction.Read)]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct) =>
        Success(await queries.GetAsync(id, ct) ?? throw await MissingAsync(id, CrudAction.Read, ct));

    [HttpPost, AllowAnonymous, EnableRateLimiting(RateLimitPolicies.ReportCreation)]
    public async Task<IActionResult> Create(CreateReportRequest request, CancellationToken ct)
    {
        var locationIds = await ResolveLocationIdsAsync(request.LocationIds!, ct);

        // Öffentliche Meldungen sind immer "employee"; ein vom Client gesendetes source wird ignoriert.
        var report = new Report(
            request.PersonnelNumber!.Value, ReportSource.Employee, request.Category!.Value,
            request.Severity!.Value, request.Description!, locationIds)
        {
            Device = request.Device,
            NoticedAt = request.NoticedAt,
            NoticedHow = request.NoticedHow,
            PriorActivity = request.PriorActivity
        };

        db.Reports.Add(report);
        await db.SaveChangesAsync(ct);

        await events.ReportCreatedAsync(report, ct);
        return CreatedData(await queries.GetAsync(report.Id, ct));
    }

    [HttpPatch("{id:guid}"), HasPrivilege<Report>(CrudAction.Update)]
    public async Task<IActionResult> Update(Guid id, UpdateReportRequest request, CancellationToken ct)
    {
        var report = await db.Reports.Include(r => r.ReportLocations).FirstOrDefaultAsync(r => r.Id == id, ct)
                     ?? throw await MissingAsync(id, CrudAction.Update, ct);

        if (request.ParentReportId is { } parentId) await SetParentAsync(report, parentId, ct);

        if (request.LocationIds is not null)
        {
            var locationIds = await ResolveLocationIdsAsync(request.LocationIds, ct);
            if (!scope.Current.All)
            {
                // Eingeschränkt darf man nur "seinen" Teil ändern: neue Filialen nur im eigenen Scope, Filialen außerhalb bleiben unangetastet.
                if (locationIds.Any(id => !scope.Current.AllowsLocation(id))) throw AuthMessages.Forbidden(CrudAction.Update);
                locationIds.AddRange(report.LocationIds.Where(id => !scope.Current.AllowsLocation(id) && !locationIds.Contains(id)));
            }

            foreach (var link in report.ReportLocations.Where(l => !locationIds.Contains(l.LocationId)).ToList()) report.ReportLocations.Remove(link);
            foreach (var locationId in locationIds) report.AddLocation(locationId);
        }

        // source ist nach dem Anlegen nicht änderbar und wird bewusst nicht übernommen.
        if (request.PersonnelNumber is not null) report.PersonnelNumber = request.PersonnelNumber.Value;
        if (request.Status is not null) report.Status = request.Status.Value;
        if (request.Category is not null) report.Category = request.Category.Value;
        if (request.Description is not null) report.Description = request.Description;
        if (request.Severity is not null) report.Severity = request.Severity.Value;
        if (request.Device is not null) report.Device = request.Device;
        if (request.NoticedAt is not null) report.NoticedAt = request.NoticedAt;
        if (request.NoticedHow is not null) report.NoticedHow = request.NoticedHow;
        if (request.PriorActivity is not null) report.PriorActivity = request.PriorActivity;
        if (request.Comment is not null) report.Comment = request.Comment;
        if (request.Archived is not null) report.Archived = request.Archived.Value;

        await db.SaveChangesAsync(ct);
        return Success(await queries.GetAsync(id, ct));
    }

    // Soft-Delete; wiederholtes Aufrufen ist idempotent.
    [HttpDelete("{id:guid}"), HasPrivilege<Report>(CrudAction.Delete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        // FindAsync wendet keine Abfragefilter an: bewusst die gefilterte Abfrage.
        var report = await db.Reports.FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw await MissingAsync(id, CrudAction.Delete, ct);
        report.Archived = true;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // Eigener Endpunkt, weil PATCH "nicht gesetzt" und "explizit null" nicht unterscheiden kann.
    [HttpDelete("{id:guid}/parent"), HasPrivilege<Report>(CrudAction.Update)]
    public async Task<IActionResult> RemoveParent(Guid id, CancellationToken ct)
    {
        var report = await db.Reports.FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw await MissingAsync(id, CrudAction.Update, ct);
        report.ParentReportId = null;
        await db.SaveChangesAsync(ct);
        return Success(await queries.GetAsync(id, ct));
    }

    // Außerhalb des eigenen Scopes: 403 mit der vordefinierten Meldung (die Meldung existiert); nicht vorhanden: 404.
    private async Task<ApiException> MissingAsync(Guid id, CrudAction action, CancellationToken ct) =>
        await db.Reports.IgnoreQueryFilters().AnyAsync(r => r.Id == id, ct) ? AuthMessages.Forbidden(action) : ApiException.NotFound();

    // Einzige Stelle, an der ein Parent gesetzt wird — keinen zweiten Codepfad ohne Zyklusprüfung anlegen.
    private async Task SetParentAsync(Report report, Guid parentId, CancellationToken ct)
    {
        if (parentId == report.Id)
            throw ApiException.BadRequest("Ein Report kann nicht sein eigener Hauptreport sein.");
        if (!await db.Reports.AnyAsync(r => r.Id == parentId, ct))
            throw ApiException.NotFound("Hauptreport wurde nicht gefunden.");

        // Vorfahrenkette des Parents ablaufen; taucht der aktuelle Report auf, wäre es ein Zyklus.
        var seen = new HashSet<Guid>();
        Guid? cursor = parentId;
        while (cursor is { } current)
        {
            if (current == report.Id) throw ApiException.BadRequest("Zyklische Verknüpfung nicht erlaubt.");
            if (!seen.Add(current)) break;
            // Die Kette läuft ohne Scope-Filter, sonst bliebe ein Zyklus über unsichtbare Meldungen unerkannt.
            cursor = await db.Reports.IgnoreQueryFilters().Where(r => r.Id == current).Select(r => r.ParentReportId).FirstOrDefaultAsync(ct);
        }

        report.ParentReportId = parentId;
    }

    // Duplikate in der ID-Liste werden zusammengefasst; archivierte oder unbekannte Filialen → 404.
    private async Task<List<Guid>> ResolveLocationIdsAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var distinct = ids.Distinct().ToList();
        // Eine unbekannte Filiale kann gerade erst angelegt worden sein: vor dem 404 einmal frisch nachsehen.
        var known = await locationDirectory.GetAllAsync(ct);
        if (distinct.Any(id => !IsUsable(known, id))) known = await locationDirectory.GetAllAsync(ct, forceRefresh: true);
        if (distinct.Any(id => !IsUsable(known, id))) throw ApiException.NotFound(LocationsNotFound);
        return distinct;
    }

    private static bool IsUsable(IReadOnlyDictionary<Guid, LocationResponse> known, Guid id) => known.TryGetValue(id, out var location) && !location.Archived;
}
