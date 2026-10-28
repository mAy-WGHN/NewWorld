using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Shared.Authorization;
using Workforce.Core.Absences;
using Workforce.Core.Unavailability;
using Workforce.Infrastructure;
using Workforce.Infrastructure.Absences;

namespace Workforce.Api;

/// <summary>
/// Unavailability: the kinds HR maintains, entries of HR and the combined timeline (sickness, vacation, entries, KU). Who sees which kind is the
/// same question as for sickness and vacation: the person themself and holders of <see cref="AbsencePrivileges.ReadReason"/> see the kind, holders of
/// <see cref="AbsencePrivileges.ReadAvailability"/> only "unavailable". This is enforced here, not in the frontend.
/// </summary>
public static class UnavailabilityEndpoints
{
    private const int MaxWindowDays = 366;

    public static WebApplication MapUnavailabilityEndpoints(this WebApplication app)
    {
        MapKinds(app);
        MapViews(app);
        MapEntries(app);
        return app;
    }

    private static void MapKinds(WebApplication app)
    {
        // Readable for everybody who enters unavailability or maintains the kinds.
        app.MapGet("/api/unavailability-kinds", async (bool? includeInactive, ClaimsPrincipal user, IAuthorizationService auth, UnavailabilityService service, CancellationToken ct) =>
            {
                if (!await Endpoints.Allows(auth, user, new object(), UnavailabilityPrivileges.ManageKinds)
                    && !await Endpoints.Allows(auth, user, new object(), UnavailabilityPrivileges.Create))
                {
                    return Results.Forbid();
                }

                return Results.Ok((await service.KindsAsync(includeInactive ?? false, ct)).Select(ToDto));
            })
            .RequireAuthorization();

        app.MapPost("/api/unavailability-kinds", async (AddUnavailabilityKindRequest request, UnavailabilityService service, WorkforceUnitOfWork uow, CancellationToken ct) =>
            {
                var result = await uow.RunAsync(() => service.AddKindAsync(request.Code, request.Description, request.PaidTime, request.ScheduleSymbol, request.WorkCode, ct), r => r.Error == UnavailabilityError.None, ct);
                return result.Error == UnavailabilityError.None ? Results.Ok(ToDto(result.Kind!)) : Problem(result.Error);
            })
            .RequireAuthorization(AppPolicies.Privilege(UnavailabilityPrivileges.ManageKinds));

        app.MapPut("/api/unavailability-kinds/{kindId:guid}", async (Guid kindId, UpdateUnavailabilityKindRequest request, UnavailabilityService service, WorkforceUnitOfWork uow, CancellationToken ct) =>
            {
                var error = await uow.RunAsync(async () =>
                {
                    var changed = await service.UpdateKindAsync(kindId, request.Description, request.PaidTime, request.ScheduleSymbol, request.WorkCode, ct);
                    return changed == UnavailabilityError.None && request.IsActive is { } active ? await service.SetKindActiveAsync(kindId, active, ct) : changed;
                }, e => e == UnavailabilityError.None, ct);
                return error == UnavailabilityError.None ? Results.NoContent() : Problem(error);
            })
            .RequireAuthorization(AppPolicies.Privilege(UnavailabilityPrivileges.ManageKinds));
    }

    private static void MapViews(WebApplication app)
    {
        // The own list: everything, also what HR entered (e.g. special leave), so the person sees it in the schedule.
        app.MapGet("/api/me/unavailabilities", async (ClaimsPrincipal user, UnavailabilityService service, CancellationToken ct) =>
                Endpoints.Me(user) is not { } me
                    ? Results.Forbid()
                    : Results.Ok((await service.TimelineAsync(me, ct)).Select(i => Full(i))))
            .RequireAuthorization(AppPolicies.Privilege(AbsencePrivileges.ReadOwn));

        app.MapGet("/api/employees/{employeeId:guid}/unavailabilities", async (Guid employeeId, ClaimsPrincipal user, IAuthorizationService auth, UnavailabilityService service, CancellationToken ct) =>
            {
                var resource = new EmployeeResource(employeeId);
                var full = Endpoints.Me(user) == employeeId || await Endpoints.Allows(auth, user, resource, AbsencePrivileges.ReadReason);
                if (!full && !await Endpoints.Allows(auth, user, resource, AbsencePrivileges.ReadAvailability))
                {
                    return Results.Forbid();
                }

                var items = await service.TimelineAsync(employeeId, ct);
                return Results.Ok(full ? items.Select(i => Full(i)) : Colleague(employeeId, items));
            })
            .RequireAuthorization();

        // Colleagues of a store (for the schedule): "unavailable" only; with the reason privilege for a person also the kind.
        app.MapGet("/api/stores/{storeId:guid}/unavailabilities", async (
                Guid storeId, DateOnly? from, DateOnly? to, ClaimsPrincipal user, IAuthorizationService auth, WorkforceDbContext db, UnavailabilityService service, AbsenceService absences, CancellationToken ct) =>
            {
                var availability = await Endpoints.Allows(auth, user, new StoreResource(storeId), AbsencePrivileges.ReadAvailability);
                var detail = await Endpoints.Allows(auth, user, new StoreResource(storeId), AbsencePrivileges.ReadReason);
                if (!availability && !detail)
                {
                    return Results.Forbid();
                }

                var start = from ?? absences.TodayLocal;
                var end = to ?? start.AddDays(30);
                if (end < start || end.DayNumber - start.DayNumber > MaxWindowDays)
                {
                    return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: nameof(UnavailabilityError.InvalidPeriod));
                }

                var window = new DateRange(start, end);
                var me = Endpoints.Me(user);
                var result = new List<StoreUnavailabilityDto>();
                foreach (var person in await db.People.AsNoTracking().Where(p => p.HomeStoreId == storeId).OrderBy(p => p.Name).ToListAsync(ct))
                {
                    var items = (await service.TimelineAsync(person.EmployeeId, ct)).Where(i => new DateRange(i.From, i.To).Overlaps(window)).ToList();
                    if (items.Count == 0)
                    {
                        continue;
                    }

                    if (me == person.EmployeeId || await Endpoints.Allows(auth, user, new EmployeeResource(person.EmployeeId), AbsencePrivileges.ReadReason))
                    {
                        result.AddRange(items.Select(i => new StoreUnavailabilityDto(person.Name, Full(i))));
                    }
                    else if (availability)
                    {
                        result.AddRange(Colleague(person.EmployeeId, items).Select(p => new StoreUnavailabilityDto(person.Name, p)));
                    }
                }

                return Results.Ok(result);
            })
            .RequireAuthorization();
    }

    private static void MapEntries(WebApplication app)
    {
        // HR enters a period of a kind for a person. Krank and Urlaub come from the absences (422 KindNotEnterable).
        app.MapPost("/api/employees/{employeeId:guid}/unavailabilities", async (
                Guid employeeId, AddUnavailabilityRequest request, ClaimsPrincipal user, IAuthorizationService auth, UnavailabilityService service, WorkforceUnitOfWork uow, CancellationToken ct) =>
            {
                if (Endpoints.Me(user) is not { } actor || !await Endpoints.Allows(auth, user, new EmployeeResource(employeeId), UnavailabilityPrivileges.Create))
                {
                    return Results.Forbid();
                }

                var result = await uow.RunAsync(() => service.AddEntryAsync(employeeId, request.KindId, request.From, request.To, actor, ct), r => r.Error == UnavailabilityError.None, ct);
                return result.Error == UnavailabilityError.None
                    ? Results.Ok(new UnavailabilityDto(employeeId, result.Entry!.From, result.Entry.To, "Unavailable", result.Entry.Id, Origin: nameof(UnavailabilityOrigin.Manual)))
                    : Problem(result.Error);
            })
            .RequireAuthorization();

        app.MapDelete("/api/employees/{employeeId:guid}/unavailabilities/{entryId:guid}", async (
                Guid employeeId, Guid entryId, IAuthorizationService auth, ClaimsPrincipal user, UnavailabilityService service, WorkforceUnitOfWork uow, CancellationToken ct) =>
            {
                if (!await Endpoints.Allows(auth, user, new EmployeeResource(employeeId), UnavailabilityPrivileges.Delete))
                {
                    return Results.Forbid();
                }

                var error = await uow.RunAsync(() => service.RemoveEntryAsync(employeeId, entryId, ct), e => e == UnavailabilityError.None, ct);
                return error == UnavailabilityError.None ? Results.NoContent() : Problem(error);
            })
            .RequireAuthorization();
    }

    private static UnavailabilityDto Full(UnavailabilityItem i) =>
        new(i.EmployeeId, i.From, i.To, "Unavailable", i.Id, i.KindCode, i.KindDescription, i.ScheduleSymbol, i.PaidTime, i.Origin.ToString());

    private static IEnumerable<UnavailabilityDto> Colleague(Guid employeeId, IEnumerable<UnavailabilityItem> items) =>
        UnavailabilityTimeline.ForColleagues(items).Select(c => new UnavailabilityDto(employeeId, c.From, c.To, "Unavailable", Kind: c.KindCode, Description: c.KindDescription, ScheduleSymbol: c.ScheduleSymbol));

    private static UnavailabilityKindDto ToDto(UnavailabilityKind k) => new(k.Id, k.Code, k.Description, k.PaidTime, k.ScheduleSymbol, k.WorkCode, k.IsActive, k.IsSystem);

    private static IResult Problem(UnavailabilityError error) => Results.Problem(
        statusCode: error switch
        {
            UnavailabilityError.NotFound or UnavailabilityError.UnknownPerson or UnavailabilityError.UnknownKind => StatusCodes.Status404NotFound,
            UnavailabilityError.Overlap or UnavailabilityError.DuplicateCode or UnavailabilityError.SystemKind or UnavailabilityError.SystemEntry => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status422UnprocessableEntity,
        },
        title: error.ToString());
}
