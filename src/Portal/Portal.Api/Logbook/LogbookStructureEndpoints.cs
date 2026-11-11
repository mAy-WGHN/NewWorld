using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Portal.Api.Http;
using Portal.Core;
using Portal.Core.Logbook;
using Portal.Infrastructure;
using Portal.Infrastructure.Logbook;
using Shared.Authorization;

namespace Portal.Api.Logbook;

public sealed record LogbookLeaderDto(Guid Id, string Name);

public sealed record LogbookDistrictDto(Guid Id, string Name, IReadOnlyList<LogbookLeaderDto> Leaders);

public sealed record LogbookRegionDto(Guid Id, Guid DistrictId, string Name, IReadOnlyList<LogbookLeaderDto> Leaders);

public sealed record LogbookStoreDto(Guid StoreId, string StoreNumber, string StoreName, Guid DistrictId, Guid? RegionId);

public sealed record LogbookStructureDto(IReadOnlyList<LogbookDistrictDto> Districts, IReadOnlyList<LogbookRegionDto> Regions, IReadOnlyList<LogbookStoreDto> Stores);

public sealed record LogbookDistrictInput(Guid Id, string? Name, List<Guid>? LeaderIds);

public sealed record LogbookRegionInput(Guid Id, Guid DistrictId, string? Name, List<Guid>? LeaderIds);

public sealed record LogbookStoreInput(Guid StoreId, Guid DistrictId, Guid? RegionId);

/// <summary>Ids come from the client (it builds the structure and saves it as a whole); lists are required, empty is fine.</summary>
public sealed record SetLogbookStructureRequest(List<LogbookDistrictInput>? Districts, List<LogbookRegionInput>? Regions, List<LogbookStoreInput>? Stores);

public sealed record LogbookReachStoreDto(Guid StoreId, string StoreNumber, string StoreName);

/// <summary><c>Level</c> is <c>none</c>, <c>regional</c> or <c>district</c> (the higher one counts); <c>Stores</c> are the stores the person reaches.</summary>
public sealed record LogbookMeDto(string Level, bool IsRegionalLeader, bool IsDistrictLeader, IReadOnlyList<LogbookReachStoreDto> Stores);

/// <summary>
/// The hierarchy of the Berichtsheft (decision 49): districts with district leaders, regions with regional leaders, stores in a district and optionally a
/// region. Maintained as a whole by the holders of <see cref="PortalPrivileges.LogbookManageStructure"/>; leaders and stores are checked against the copies.
/// <c>GET /api/logbook/me</c> says which stores a regional or district leader reaches.
/// </summary>
public static class LogbookStructureEndpoints
{
    public static WebApplication MapLogbookStructureEndpoints(this WebApplication app)
    {
        app.MapGet("/api/logbook/structure", async (LogbookStructureStore store, PortalDbContext db, CancellationToken ct) =>
                Answer.Ok(await ToDtoAsync(await store.LoadAsync(ct), db, ct)))
            .RequireAuthorization(AppPolicies.Privilege(PortalPrivileges.LogbookManageStructure));

        app.MapPut("/api/logbook/structure", async (SetLogbookStructureRequest request, LogbookStructureStore store, PortalDbContext db, CancellationToken ct) =>
            {
                var structure = await ParseAsync(request, db, ct);
                await store.SaveAsync(structure, ct);
                return Answer.Ok(await ToDtoAsync(await store.LoadAsync(ct), db, ct));
            })
            .RequireAuthorization(AppPolicies.Privilege(PortalPrivileges.LogbookManageStructure));

        app.MapGet("/api/logbook/me", async (ClaimsPrincipal user, LogbookStructureStore store, PortalDbContext db, CancellationToken ct) =>
            {
                if (!Guid.TryParse(user.FindFirst(AppClaimTypes.EmployeeId)?.Value, out var me))
                {
                    return Results.Forbid();
                }

                var reach = LogbookReach.Of(await store.LoadAsync(ct), me);
                var ids = reach.StoreIds.ToList();
                var stores = await db.Stores.AsNoTracking().Where(s => ids.Contains(s.StoreId)).OrderBy(s => s.StoreNumber).ToListAsync(ct);
                return Answer.Ok(new LogbookMeDto(
                    reach.Level.ToString().ToLowerInvariant(), reach.IsRegionalLeader, reach.IsDistrictLeader,
                    [.. stores.Select(s => new LogbookReachStoreDto(s.StoreId, s.StoreNumber, s.Name))]));
            })
            .RequireAuthorization(AppPolicies.Privilege(PortalPrivileges.LogbookReview));

        return app;
    }

    /// <summary>The request as a structure, after the checks: lists given, ids set, rules of the structure, people and stores known.</summary>
    private static async Task<LogbookStructure> ParseAsync(SetLogbookStructureRequest request, PortalDbContext db, CancellationToken ct)
    {
        var errors = new List<ApiError>();
        if (request.Districts is null)
        {
            errors.Add(new("districts", Validator.Required));
        }

        if (request.Regions is null)
        {
            errors.Add(new("regions", Validator.Required));
        }

        if (request.Stores is null)
        {
            errors.Add(new("stores", Validator.Required));
        }

        if (errors.Count > 0)
        {
            throw ApiException.Validation(errors);
        }

        if (request.Districts!.Any(d => d.Id == Guid.Empty))
        {
            errors.Add(new("districts", "Jeder Bezirk braucht eine Id."));
        }

        if (request.Regions!.Any(r => r.Id == Guid.Empty))
        {
            errors.Add(new("regions", "Jede Region braucht eine Id."));
        }

        if (errors.Count > 0)
        {
            throw ApiException.Validation(errors);
        }

        var structure = new LogbookStructure(
            [.. request.Districts!.Select(d => new LogbookDistrict(d.Id, d.Name ?? string.Empty, [.. d.LeaderIds ?? []]))],
            [.. request.Regions!.Select(r => new LogbookRegion(r.Id, r.DistrictId, r.Name ?? string.Empty, [.. r.LeaderIds ?? []]))],
            [.. request.Stores!.Select(s => new LogbookStoreAssignment(s.StoreId, s.DistrictId, s.RegionId))]);

        foreach (var problem in LogbookStructureValidator.Validate(structure))
        {
            errors.Add(new(FieldOf(problem.Code), MessageOf(problem.Code)));
        }

        var leaderIds = structure.Districts.SelectMany(d => d.LeaderIds).Concat(structure.Regions.SelectMany(r => r.LeaderIds)).Distinct().ToList();
        if (leaderIds.Count > 0 && await db.People.CountAsync(p => leaderIds.Contains(p.EmployeeId), ct) != leaderIds.Count)
        {
            errors.Add(new("districts", "Mindestens eine Leitung ist unbekannt."));
        }

        var storeIds = structure.Stores.Select(s => s.StoreId).Distinct().ToList();
        if (storeIds.Count > 0 && await db.Stores.CountAsync(s => storeIds.Contains(s.StoreId), ct) != storeIds.Count)
        {
            errors.Add(new("stores", "Mindestens eine Filiale ist unbekannt."));
        }

        if (errors.Count > 0)
        {
            throw ApiException.Validation([.. errors.DistinctBy(e => (e.Field, e.Message))]);
        }

        return structure;
    }

    private static string FieldOf(LogbookStructureError error) => error switch
    {
        LogbookStructureError.RegionNameMissing or LogbookStructureError.RegionNameNotUnique => "regions",
        LogbookStructureError.UnknownRegion or LogbookStructureError.RegionNotInDistrict or LogbookStructureError.StoreAssignedTwice => "stores",
        _ => "districts",
    };

    private static string MessageOf(LogbookStructureError error) => error switch
    {
        LogbookStructureError.DistrictNameMissing => "Jeder Bezirk braucht einen Namen.",
        LogbookStructureError.DistrictNameNotUnique => "Bezirksnamen dürfen sich nicht wiederholen.",
        LogbookStructureError.RegionNameMissing => "Jede Region braucht einen Namen.",
        LogbookStructureError.RegionNameNotUnique => "Regionsnamen dürfen sich innerhalb eines Bezirks nicht wiederholen.",
        LogbookStructureError.DuplicateId => "Eine Id kommt doppelt vor.",
        LogbookStructureError.UnknownDistrict => "Ein Bezirk, auf den verwiesen wird, ist nicht in der Liste.",
        LogbookStructureError.UnknownRegion => "Eine Region, auf die verwiesen wird, ist nicht in der Liste.",
        LogbookStructureError.RegionNotInDistrict => "Die Region einer Filiale muss im Bezirk der Filiale liegen.",
        LogbookStructureError.StoreAssignedTwice => "Eine Filiale darf nur einmal zugeordnet sein.",
        LogbookStructureError.LeaderListedTwice => "Eine Leitung darf je Bezirk oder Region nur einmal vorkommen.",
        _ => "Die Struktur ist nicht gültig.",
    };

    private static async Task<LogbookStructureDto> ToDtoAsync(LogbookStructure structure, PortalDbContext db, CancellationToken ct)
    {
        var people = await db.People.AsNoTracking().ToDictionaryAsync(p => p.EmployeeId, p => p.Name, ct);
        var stores = await db.Stores.AsNoTracking().ToDictionaryAsync(s => s.StoreId, ct);
        IReadOnlyList<LogbookLeaderDto> Leaders(IEnumerable<Guid> ids) =>
            [.. ids.Select(id => new LogbookLeaderDto(id, people.GetValueOrDefault(id, "(unbekannt)"))).OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase)];

        return new LogbookStructureDto(
            [.. structure.Districts.Select(d => new LogbookDistrictDto(d.Id, d.Name, Leaders(d.LeaderIds)))],
            [.. structure.Regions.Select(r => new LogbookRegionDto(r.Id, r.DistrictId, r.Name, Leaders(r.LeaderIds)))],
            [.. structure.Stores
                .Select(s => (Assignment: s, Store: stores.GetValueOrDefault(s.StoreId)))
                .OrderBy(x => x.Store?.StoreNumber, StringComparer.Ordinal)
                .Select(x => new LogbookStoreDto(x.Assignment.StoreId, x.Store?.StoreNumber ?? string.Empty, x.Store?.Name ?? "(unbekannt)", x.Assignment.DistrictId, x.Assignment.RegionId))]);
    }
}
