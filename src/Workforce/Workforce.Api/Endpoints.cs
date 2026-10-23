using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Shared.Approvals;
using Shared.Authorization;
using Workforce.Contracts;
using Workforce.Core.Absences;
using Workforce.Infrastructure;
using Workforce.Infrastructure.Absences;

namespace Workforce.Api;

/// <summary>
/// Absences. Every privilege check is local (permission copy from Access). The reason (type) of an absence is only returned to people
/// with <see cref="AbsencePrivileges.ReadReason"/> for that person: the person themself, supervisors and HR. This is enforced here,
/// not in the frontend. Colleagues with <see cref="AbsencePrivileges.ReadAvailability"/> only see "absent".
/// </summary>
public static class Endpoints
{
    private const int MaxWindowDays = 366;

    public static WebApplication MapAbsenceEndpoints(this WebApplication app)
    {
        MapOwn(app.MapGroup("/api/me"));
        MapOthers(app.MapGroup("/api"));

        // Details including the type for authorised services (later HR API).
        app.MapGet("/internal/absences/{absenceId:guid}", async (Guid absenceId, WorkforceDbContext db, CancellationToken ct) =>
                await db.Absences.AsNoTracking().FirstOrDefaultAsync(a => a.Id == absenceId, ct) is { } a
                    ? Results.Ok(new AbsenceDetailsDto(a.Id, a.EmployeeId, a.Type.ToString(), a.Status.ToString(), a.From, a.To, a.CertificateOnFile, a.Version, a.SyncStatus.ToString(), a.SyncedVersion))
                    : Results.NotFound())
            .RequireAuthorization(AppPolicies.ServiceRole(WorkforceRoles.AbsenceDetailsRead));
        return app;
    }

    private static void MapOwn(RouteGroupBuilder me)
    {
        me.MapGet("/absences", async (ClaimsPrincipal user, WorkforceDbContext db, CancellationToken ct) =>
            {
                if (Me(user) is not { } id)
                {
                    return Results.Forbid();
                }

                var rows = await db.Absences.AsNoTracking().Where(a => a.EmployeeId == id).OrderByDescending(a => a.From).ToListAsync(ct);
                return Results.Ok(rows.Select(a => ToDto(a, reason: true, sync: false)));
            })
            .RequireAuthorization(AppPolicies.Privilege(AbsencePrivileges.ReadOwn));

        me.MapPost("/absences/sick", (ReportSickRequest request, ClaimsPrincipal user, AbsenceService service, WorkforceUnitOfWork uow, CancellationToken ct) =>
                Mutate(user, id => uow.RunAsync(() => service.ReportSickAsync(id, request.From, request.To, request.CertificateOnFile, ct), ct)))
            .RequireAuthorization(AppPolicies.Privilege(AbsencePrivileges.CreateOwn));

        me.MapPost("/absences/vacation", (RequestVacationRequest request, ClaimsPrincipal user, AbsenceService service, WorkforceUnitOfWork uow, CancellationToken ct) =>
                Mutate(user, id => uow.RunAsync(() => service.RequestVacationAsync(id, request.From, request.To, ct), ct)))
            .RequireAuthorization(AppPolicies.Privilege(AbsencePrivileges.CreateOwn));

        // Someone else's absence looks like a missing one (404): its existence is not revealed.
        me.MapPost("/absences/{absenceId:guid}/withdraw", (Guid absenceId, ClaimsPrincipal user, WorkforceDbContext db, AbsenceService service, WorkforceUnitOfWork uow, CancellationToken ct) =>
                MutateOwn(user, db, absenceId, id => uow.RunAsync(() => service.WithdrawVacationAsync(absenceId, id, ct), ct), ct))
            .RequireAuthorization(AppPolicies.Privilege(AbsencePrivileges.UpdateOwn));

        me.MapPost("/absences/{absenceId:guid}/end", (Guid absenceId, EndSickRequest request, ClaimsPrincipal user, WorkforceDbContext db, AbsenceService service, WorkforceUnitOfWork uow, CancellationToken ct) =>
                MutateOwn(user, db, absenceId, _ => uow.RunAsync(() => service.EndSickNoteAsync(absenceId, request.To, ct), ct), ct))
            .RequireAuthorization(AppPolicies.Privilege(AbsencePrivileges.UpdateOwn));

        me.MapGet("/vacation-balance", async (int? year, ClaimsPrincipal user, AbsenceService service, CancellationToken ct) =>
                Me(user) is not { } id ? Results.Forbid() : await Balance(service, id, year, ct))
            .RequireAuthorization(AppPolicies.Privilege(AbsencePrivileges.ReadOwn));
    }

    private static void MapOthers(RouteGroupBuilder api)
    {
        // Colleagues of a store: "absent" only; with the reason privilege for a person also type and status.
        api.MapGet("/stores/{storeId:guid}/absences", async (
                Guid storeId, DateOnly? from, DateOnly? to, ClaimsPrincipal user, IAuthorizationService auth, WorkforceDbContext db, AbsenceService service, CancellationToken ct) =>
            {
                var availability = await Allows(auth, user, new StoreResource(storeId), AbsencePrivileges.ReadAvailability);
                var detail = await Allows(auth, user, new StoreResource(storeId), AbsencePrivileges.ReadReason);
                if (!availability && !detail)
                {
                    return Results.Forbid();
                }

                var start = from ?? service.TodayLocal;
                var end = to ?? start.AddDays(30);
                if (end < start || end.DayNumber - start.DayNumber > MaxWindowDays)
                {
                    return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: nameof(AbsenceError.InvalidPeriod));
                }

                var people = await db.People.AsNoTracking().Where(p => p.HomeStoreId == storeId).ToDictionaryAsync(p => p.EmployeeId, ct);
                var ids = people.Keys.ToList();
                var rows = await db.Absences.AsNoTracking()
                    .Where(a => ids.Contains(a.EmployeeId) && (a.Status == AbsenceStatus.Reported || a.Status == AbsenceStatus.Approved || a.Status == AbsenceStatus.Requested)
                                && a.From <= end && (a.To == null || a.To >= start))
                    .OrderBy(a => a.From).ToListAsync(ct);

                var result = new List<StoreAbsenceDto>();
                foreach (var a in rows)
                {
                    if (await Allows(auth, user, new EmployeeResource(a.EmployeeId), AbsencePrivileges.ReadReason))
                    {
                        result.Add(new StoreAbsenceDto(a.EmployeeId, people[a.EmployeeId].Name, a.From, a.To, a.Status.ToString(), a.Type.ToString()));
                    }
                    else if (availability && a.Status is AbsenceStatus.Reported or AbsenceStatus.Approved)
                    {
                        // Sickness (Reported) and vacation (Approved) look the same, and pending requests are not shown at all.
                        result.Add(new StoreAbsenceDto(a.EmployeeId, people[a.EmployeeId].Name, a.From, a.To, "Absent", null));
                    }
                }

                return Results.Ok(result);
            })
            .RequireAuthorization();

        api.MapGet("/employees/{employeeId:guid}/absences", async (Guid employeeId, ClaimsPrincipal user, IAuthorizationService auth, WorkforceDbContext db, CancellationToken ct) =>
            {
                if (!await Allows(auth, user, new EmployeeResource(employeeId), AbsencePrivileges.ReadReason))
                {
                    return Results.Forbid();
                }

                var sync = await Allows(auth, user, new EmployeeResource(employeeId), AbsencePrivileges.ReadSync);
                var rows = await db.Absences.AsNoTracking().Where(a => a.EmployeeId == employeeId).OrderByDescending(a => a.From).ToListAsync(ct);
                return Results.Ok(rows.Select(a => ToDto(a, reason: true, sync)));
            })
            .RequireAuthorization();

        // Supervisor or HR records a sick note for an employee.
        api.MapPost("/employees/{employeeId:guid}/absences/sick", async (
                Guid employeeId, ReportSickRequest request, ClaimsPrincipal user, IAuthorizationService auth, AbsenceService service, WorkforceUnitOfWork uow, CancellationToken ct) =>
                !await Allows(auth, user, new EmployeeResource(employeeId), AbsencePrivileges.Create)
                    ? Results.Forbid()
                    : ToResult(await uow.RunAsync(() => service.ReportSickAsync(employeeId, request.From, request.To, request.CertificateOnFile, ct), ct)))
            .RequireAuthorization();

        api.MapGet("/approvals/pending", async (ClaimsPrincipal user, WorkforceDbContext db, CancellationToken ct) =>
            {
                if (Me(user) is not { } me)
                {
                    return Results.Forbid();
                }

                var rows = await (
                    from a in db.Absences.AsNoTracking()
                    where a.Status == AbsenceStatus.Requested && a.Type == AbsenceType.Vacation
                    join r in db.Set<ApprovalRequest>().AsNoTracking() on a.ApprovalRequestId equals r.Id
                    where r.Steps.Any(s => s.Decision == null && s.ApproverIds.Contains(me))
                    join p in db.People.AsNoTracking() on a.EmployeeId equals p.EmployeeId
                    orderby a.From
                    select new PendingApprovalDto(a.Id, a.EmployeeId, p.Name, a.From, a.To!.Value)).ToListAsync(ct);
                return Results.Ok(rows);
            })
            .RequireAuthorization(AppPolicies.Privilege(AbsencePrivileges.Approve));

        api.MapPost("/absences/{absenceId:guid}/approve", (Guid absenceId, ClaimsPrincipal user, IAuthorizationService auth, WorkforceDbContext db, AbsenceService service, WorkforceUnitOfWork uow, CancellationToken ct) =>
                Decide(absenceId, approve: true, user, auth, db, service, uow, ct))
            .RequireAuthorization();
        api.MapPost("/absences/{absenceId:guid}/reject", (Guid absenceId, ClaimsPrincipal user, IAuthorizationService auth, WorkforceDbContext db, AbsenceService service, WorkforceUnitOfWork uow, CancellationToken ct) =>
                Decide(absenceId, approve: false, user, auth, db, service, uow, ct))
            .RequireAuthorization();

        api.MapGet("/employees/{employeeId:guid}/vacation-balance", async (
                Guid employeeId, int? year, ClaimsPrincipal user, IAuthorizationService auth, AbsenceService service, CancellationToken ct) =>
                !await Allows(auth, user, new EmployeeResource(employeeId), AbsencePrivileges.ReadReason) ? Results.Forbid() : await Balance(service, employeeId, year, ct))
            .RequireAuthorization();

        api.MapPut("/employees/{employeeId:guid}/vacation-entitlements/{year:int}", async (
                Guid employeeId, int year, SetEntitlementRequest request, ClaimsPrincipal user, IAuthorizationService auth, AbsenceService service, WorkforceUnitOfWork uow, CancellationToken ct) =>
            {
                if (!await Allows(auth, user, new EmployeeResource(employeeId), AbsencePrivileges.ManageEntitlement))
                {
                    return Results.Forbid();
                }

                var error = await uow.RunAsync(async () => AbsenceResult.Fail(await service.SetEntitlementAsync(employeeId, year, request.Days, ct)), r => r.Error == AbsenceError.None, ct);
                return error.Error == AbsenceError.None ? Results.NoContent() : Problem(error.Error);
            })
            .RequireAuthorization();
    }

    private static async Task<IResult> Decide(
        Guid absenceId, bool approve, ClaimsPrincipal user, IAuthorizationService auth, WorkforceDbContext db, AbsenceService service, WorkforceUnitOfWork uow, CancellationToken ct)
    {
        if (Me(user) is not { } approver)
        {
            return Results.Forbid();
        }

        if (await db.Absences.AsNoTracking().Where(a => a.Id == absenceId).Select(a => (Guid?)a.EmployeeId).FirstOrDefaultAsync(ct) is not { } employeeId)
        {
            return Results.NotFound();
        }

        // Two conditions: the privilege reaches the person AND the caller is in the approval chain of this request (checked by the service).
        return !await Allows(auth, user, new EmployeeResource(employeeId), AbsencePrivileges.Approve)
            ? Results.Forbid()
            : ToResult(await uow.RunAsync(() => service.DecideVacationAsync(absenceId, approver, approve, ct), ct));
    }

    private static async Task<IResult> Balance(AbsenceService service, Guid employeeId, int? year, CancellationToken ct) =>
        await service.BalanceAsync(employeeId, year ?? service.TodayLocal.Year, ct) is { } b
            ? Results.Ok(new VacationBalanceDto(b.Year, b.Entitlement, b.Taken, b.Planned, b.Remaining))
            : Problem(AbsenceError.NoEntitlement);

    private static async Task<IResult> Mutate(ClaimsPrincipal user, Func<Guid, Task<AbsenceResult>> action) =>
        Me(user) is not { } id ? Results.Forbid() : ToResult(await action(id));

    private static async Task<IResult> MutateOwn(ClaimsPrincipal user, WorkforceDbContext db, Guid absenceId, Func<Guid, Task<AbsenceResult>> action, CancellationToken ct)
    {
        if (Me(user) is not { } id)
        {
            return Results.Forbid();
        }

        return await db.Absences.AsNoTracking().AnyAsync(a => a.Id == absenceId && a.EmployeeId == id, ct)
            ? ToResult(await action(id))
            : Results.NotFound();
    }

    private static IResult ToResult(AbsenceResult result) =>
        result.Error == AbsenceError.None ? Results.Ok(ToDto(result.Absence!, reason: true, sync: false)) : Problem(result.Error);

    private static IResult Problem(AbsenceError error) => Results.Problem(
        statusCode: error switch
        {
            AbsenceError.NotFound or AbsenceError.UnknownPerson => StatusCodes.Status404NotFound,
            AbsenceError.NotAllowed => StatusCodes.Status403Forbidden,
            AbsenceError.InvalidPeriod or AbsenceError.AfterExit or AbsenceError.NoEntitlement or AbsenceError.InsufficientBalance => StatusCodes.Status422UnprocessableEntity,
            _ => StatusCodes.Status409Conflict,
        },
        title: error.ToString());

    private static AbsenceDto ToDto(Absence a, bool reason, bool sync) => new(
        a.Id, a.EmployeeId, reason ? a.Type.ToString() : null, a.Status.ToString(), a.From, a.To, reason ? a.CertificateOnFile : null,
        sync ? a.Version : null, sync ? a.SyncStatus.ToString() : null);

    private static async Task<bool> Allows(IAuthorizationService auth, ClaimsPrincipal user, object resource, string privilege) =>
        (await auth.AuthorizeAsync(user, resource, AppPolicies.Privilege(privilege))).Succeeded;

    private static Guid? Me(ClaimsPrincipal user) => Guid.TryParse(user.FindFirst(AppClaimTypes.EmployeeId)?.Value, out var id) ? id : null;
}
