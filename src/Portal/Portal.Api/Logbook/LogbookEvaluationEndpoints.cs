using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Portal.Api.Http;
using Portal.Core;
using Portal.Core.Logbook;
using Portal.Infrastructure;
using Portal.Infrastructure.Logbook;
using Shared.Authorization;

namespace Portal.Api.Logbook;

/// <summary>The figures of a store, a region or a district; <c>OnTimeRate</c> is 0 to 1, <c>Distribution</c> counts the ratings 1 to 5 in this order.</summary>
public sealed record LogbookFiguresDto(
    int Submitted, int OnTime, int Late, int Missing, int Expected, double? OnTimeRate, double? RatingAverage, int RatedCount, IReadOnlyList<int> Distribution,
    int Escalated, int Answered, int Done);

public sealed record LogbookStoreFiguresDto(Guid StoreId, string StoreNumber, string StoreName, Guid DistrictId, Guid? RegionId, LogbookFiguresDto Figures);

public sealed record LogbookGroupFiguresDto(Guid Id, string Name, int Stores, LogbookFiguresDto Figures);

/// <summary>
/// <c>Total</c> is over all stores the person sees; <c>Regions</c> only for regions the person leads (or whose district they lead), <c>Districts</c> only for
/// districts they lead; a store manager gets only the stores. <c>AsOf</c> is the time the figures were calculated: what is missing counts until then.
/// </summary>
public sealed record LogbookEvaluationDto(
    int Year, DateTimeOffset AsOf, LogbookFiguresDto Total, IReadOnlyList<LogbookStoreFiguresDto> Stores, IReadOnlyList<LogbookGroupFiguresDto> Regions,
    IReadOnlyList<LogbookGroupFiguresDto> Districts);

/// <summary>
/// The evaluation of a calendar year (decision 49): per store, per region and per district how many reports were handed in on time, how they were rated on
/// average, and more. Everybody sees the stores of their reach: a store manager the own stores, a regional leader the stores of the region, a district leader
/// the district; whoever reaches nothing gets <c>403</c>. The figures of reports that were deleted after their retention are kept as numbers.
/// </summary>
public static class LogbookEvaluationEndpoints
{
    public const int FirstYear = 2020;

    public static WebApplication MapLogbookEvaluationEndpoints(this WebApplication app)
    {
        app.MapGet("/api/logbook/evaluation", async (
                int? year, ClaimsPrincipal user, IPermissionCache cache, LogbookStructureStore structures, LogbookEvaluationService service, LogbookReportService reports,
                PortalDbContext db, TimeProvider time, LogbookOptions options, CancellationToken ct) =>
            {
                var now = time.GetUtcNow();
                var thisYear = TimeZoneInfo.ConvertTime(now, options.Zone).Year;
                var chosen = year ?? thisYear;
                if (chosen < FirstYear || chosen > thisYear)
                {
                    throw ApiException.Validation("year", $"Das Jahr muss zwischen {FirstYear} und {thisYear} liegen.");
                }

                var me = Guid.TryParse(user.FindFirst(AppClaimTypes.EmployeeId)?.Value, out var id) ? id : Guid.Empty;
                var structure = await structures.LoadAsync(ct);
                var leader = LogbookReach.Of(structure, me);
                var stores = new HashSet<Guid>(leader.StoreIds);
                if (me != Guid.Empty && cache.TryGet(me, out var snapshot) && snapshot is not null && snapshot.Privileges.TryGetValue(PortalPrivileges.LogbookWrite, out var write))
                {
                    stores.UnionWith(write.Unrestricted ? structure.Stores.Select(s => s.StoreId) : write.StoreIds.Where(s => structure.Stores.Any(a => a.StoreId == s)));
                }

                if (stores.Count == 0)
                {
                    throw new ApiException(StatusCodes.Status403Forbidden, EnvelopeAuthorizationResultHandler.Message("read"));
                }

                var figures = (await service.YearAsync(chosen, [.. stores], ct)).ToDictionary(f => f.StoreId, f => f.Figures);
                var names = await db.Stores.AsNoTracking().Where(s => stores.Contains(s.StoreId)).ToDictionaryAsync(s => s.StoreId, ct);
                var assignments = structure.Stores.Where(s => stores.Contains(s.StoreId)).ToList();

                var leadsDistrict = structure.Districts.Where(d => d.LeaderIds.Contains(me)).Select(d => d.Id).ToHashSet();
                var leadsRegion = structure.Regions.Where(r => r.LeaderIds.Contains(me) || leadsDistrict.Contains(r.DistrictId)).Select(r => r.Id).ToHashSet();

                return Answer.Ok(new LogbookEvaluationDto(
                    chosen, now, ToDto(LogbookFigures.Combine(figures.Values)),
                    [.. assignments
                        .Select(a => new LogbookStoreFiguresDto(
                            a.StoreId, names.GetValueOrDefault(a.StoreId)?.StoreNumber ?? string.Empty, names.GetValueOrDefault(a.StoreId)?.Name ?? "(unbekannt)", a.DistrictId, a.RegionId, ToDto(figures[a.StoreId])))
                        .OrderBy(s => s.StoreNumber, StringComparer.Ordinal)],
                    [.. structure.Regions.Where(r => leadsRegion.Contains(r.Id)).Select(r => Group(r.Id, r.Name, assignments.Where(a => a.RegionId == r.Id), figures)).Where(g => g.Stores > 0).OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)],
                    [.. structure.Districts.Where(d => leadsDistrict.Contains(d.Id)).Select(d => Group(d.Id, d.Name, assignments.Where(a => a.DistrictId == d.Id), figures)).Where(g => g.Stores > 0).OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)]));
            })
            .RequireAuthorization();

        return app;
    }

    private static LogbookGroupFiguresDto Group(Guid id, string name, IEnumerable<LogbookStoreAssignment> members, Dictionary<Guid, LogbookFigures> figures)
    {
        var list = members.ToList();
        return new LogbookGroupFiguresDto(id, name, list.Count, ToDto(LogbookFigures.Combine(list.Select(a => figures[a.StoreId]))));
    }

    private static LogbookFiguresDto ToDto(LogbookFigures f) => new(
        f.Submitted, f.OnTime, f.Late, f.Missing, f.Expected, f.OnTimeRate, f.RatingAverage, f.RatedCount, f.Distribution, f.Escalated, f.Answered, f.Done);
}
