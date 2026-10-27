using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Shared.Approvals;
using Shared.Authorization;
using Workforce.Contracts;
using Workforce.Core.Absences;
using Workforce.Core.Certificates;
using Workforce.Infrastructure;
using Workforce.Infrastructure.People;
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
                    ? Results.Ok(new AbsenceDetailsDto(a.Id, a.EmployeeId, a.Type.ToString(), a.Status.ToString(), a.From, a.To, a.CertificateStatus.ToString(), a.Version, a.SyncStatus.ToString(), a.SyncedVersion))
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
                return Results.Ok(rows.Select(a => ToDto(a, reason: true, sync: false, certificate: true)));
            })
            .RequireAuthorization(AppPolicies.Privilege(AbsencePrivileges.ReadOwn));

        me.MapPost("/absences/sick", (ReportSickRequest request, ClaimsPrincipal user, AbsenceService service, WorkforceUnitOfWork uow, CancellationToken ct) =>
                Mutate(user, id => uow.RunAsync(() => service.ReportSickAsync(id, request.From, request.To, ct), ct)))
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
                var certificate = await Allows(auth, user, new EmployeeResource(employeeId), CertificatePrivileges.ReadStatus);
                var rows = await db.Absences.AsNoTracking().Where(a => a.EmployeeId == employeeId).OrderByDescending(a => a.From).ToListAsync(ct);
                return Results.Ok(rows.Select(a => ToDto(a, reason: true, sync, certificate)));
            })
            .RequireAuthorization();

        // Supervisor or HR records a sick note for an employee.
        api.MapPost("/employees/{employeeId:guid}/absences/sick", async (
                Guid employeeId, ReportSickRequest request, ClaimsPrincipal user, IAuthorizationService auth, AbsenceService service, WorkforceUnitOfWork uow, CancellationToken ct) =>
                !await Allows(auth, user, new EmployeeResource(employeeId), AbsencePrivileges.Create)
                    ? Results.Forbid()
                    : ToResult(await uow.RunAsync(() => service.ReportSickAsync(employeeId, request.From, request.To, ct), ct)))
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

        // Additional vacation days (leftover, special leave) on top of the contract: used first, expire after the first quarter of the next year.
        static ExtraVacationDto ToExtraDto(ExtraVacationDays e, VacationRules rules) => new(e.Id, e.Kind.ToString(), e.Year, e.Days, e.UsableFrom, e.ExpiresOn(rules));

        static async Task<IResult> Rules(AbsenceService service, CancellationToken ct)
        {
            var r = await service.GetVacationRulesAsync(ct);
            return Results.Ok(new VacationRulesDto(
                r.Rules.ExpiryMonth, r.Rules.ExpiryDay, r.Rules.WaitingPeriodMonths, r.Rules.WaitingMode.ToString(), r.Rules.Rounding.ToString(), r.Rules.RoundingStep, r.IsDefault, r.ChangedAt, r.ChangedBy));
        }

        // Changes of the annual entitlement during a year (other working days per week etc.): HR enters them per person.
        static EntitlementChangeDto ToChangeDto(EntitlementChange c) => new(c.Id, c.EffectiveFrom, c.AnnualDays);

        api.MapGet("/employees/{employeeId:guid}/vacation-entitlement-changes", async (Guid employeeId, ClaimsPrincipal user, IAuthorizationService auth, AbsenceService service, CancellationToken ct) =>
                !await Allows(auth, user, new EmployeeResource(employeeId), AbsencePrivileges.ManageEntitlement)
                    ? Results.Forbid()
                    : Results.Ok((await service.ListEntitlementChangesAsync(employeeId, ct)).Select(ToChangeDto)))
            .RequireAuthorization();

        api.MapPost("/employees/{employeeId:guid}/vacation-entitlement-changes", async (
                Guid employeeId, AddEntitlementChangeRequest request, ClaimsPrincipal user, IAuthorizationService auth, AbsenceService service, WorkforceUnitOfWork uow, CancellationToken ct) =>
            {
                if (Me(user) is not { } me || !await Allows(auth, user, new EmployeeResource(employeeId), AbsencePrivileges.ManageEntitlement))
                {
                    return Results.Forbid();
                }

                var result = await uow.RunAsync(() => service.AddEntitlementChangeAsync(employeeId, request.EffectiveFrom, request.AnnualDays, me, ct), r => r.Error == AbsenceError.None, ct);
                return result.Error != AbsenceError.None
                    ? Problem(result.Error)
                    : Results.Ok(ToChangeDto((await service.ListEntitlementChangesAsync(employeeId, ct)).Single(c => c.Id == result.Id)));
            })
            .RequireAuthorization();

        api.MapDelete("/employees/{employeeId:guid}/vacation-entitlement-changes/{changeId:guid}", async (
                Guid employeeId, Guid changeId, ClaimsPrincipal user, IAuthorizationService auth, AbsenceService service, WorkforceUnitOfWork uow, CancellationToken ct) =>
            {
                if (!await Allows(auth, user, new EmployeeResource(employeeId), AbsencePrivileges.ManageEntitlement))
                {
                    return Results.Forbid();
                }

                var error = await uow.RunAsync(async () => AbsenceResult.Fail(await service.RemoveEntitlementChangeAsync(employeeId, changeId, ct)), r => r.Error == AbsenceError.None, ct);
                return error.Error == AbsenceError.None ? Results.NoContent() : Problem(error.Error);
            })
            .RequireAuthorization();

        // The rules for everybody: when additional days expire (default: never), waiting period (default: none), rounding of pro rata days (default: up to half days).
        api.MapGet("/vacation-rules", Rules).RequireAuthorization(AppPolicies.Privilege(AbsencePrivileges.ManageVacationRules));
        api.MapPut("/vacation-rules", async (SetVacationRulesRequest request, ClaimsPrincipal user, AbsenceService service, WorkforceUnitOfWork uow, CancellationToken ct) =>
            {
                if (Me(user) is not { } me)
                {
                    return Results.Forbid();
                }

                var defaults = VacationRules.NeverExpire;
                if ((request.WaitingPeriodMode is { } mode && !Enum.TryParse<WaitingPeriodMode>(mode, ignoreCase: true, out _))
                    || (request.ProRataRounding is { } rounding && !Enum.TryParse<ProRataRounding>(rounding, ignoreCase: true, out _)))
                {
                    return Problem(AbsenceError.InvalidVacationRules);
                }

                var rules = new VacationRules(
                    request.ExtraDaysExpiryMonth, request.ExtraDaysExpiryDay,
                    request.WaitingPeriodMonths ?? defaults.WaitingPeriodMonths,
                    request.WaitingPeriodMode is { } m ? Enum.Parse<WaitingPeriodMode>(m, ignoreCase: true) : defaults.WaitingMode,
                    request.ProRataRounding is { } rd ? Enum.Parse<ProRataRounding>(rd, ignoreCase: true) : defaults.Rounding,
                    request.ProRataRoundingStep ?? defaults.RoundingStep);
                var error = await uow.RunAsync(async () => AbsenceResult.Fail(await service.SetVacationRulesAsync(rules, me, ct)), r => r.Error == AbsenceError.None, ct);
                return error.Error == AbsenceError.None ? await Rules(service, ct) : Problem(error.Error);
            })
            .RequireAuthorization(AppPolicies.Privilege(AbsencePrivileges.ManageVacationRules));

        api.MapGet("/employees/{employeeId:guid}/vacation-extra-days", async (Guid employeeId, ClaimsPrincipal user, IAuthorizationService auth, AbsenceService service, CancellationToken ct) =>
            {
                if (!await Allows(auth, user, new EmployeeResource(employeeId), AbsencePrivileges.ManageEntitlement))
                {
                    return Results.Forbid();
                }

                var rules = (await service.GetVacationRulesAsync(ct)).Rules;
                return Results.Ok((await service.ListExtraVacationAsync(employeeId, ct)).Select(e => ToExtraDto(e, rules)));
            })
            .RequireAuthorization();

        api.MapPost("/employees/{employeeId:guid}/vacation-extra-days", async (
                Guid employeeId, AddExtraVacationRequest request, ClaimsPrincipal user, IAuthorizationService auth, AbsenceService service, WorkforceUnitOfWork uow, CancellationToken ct) =>
            {
                if (Me(user) is not { } me || !await Allows(auth, user, new EmployeeResource(employeeId), AbsencePrivileges.ManageEntitlement))
                {
                    return Results.Forbid();
                }

                if (!Enum.TryParse<ExtraVacationKind>(request.Kind, ignoreCase: true, out var kind))
                {
                    return Problem(AbsenceError.InvalidPeriod);
                }

                var result = await uow.RunAsync(() => service.AddExtraVacationAsync(employeeId, kind, request.Year, request.Days, me, ct), r => r.Error == AbsenceError.None, ct);
                return result.Error != AbsenceError.None
                    ? Problem(result.Error)
                    : Results.Ok(ToExtraDto((await service.ListExtraVacationAsync(employeeId, ct)).Single(e => e.Id == result.Id), (await service.GetVacationRulesAsync(ct)).Rules));
            })
            .RequireAuthorization();

        api.MapDelete("/employees/{employeeId:guid}/vacation-extra-days/{extraId:guid}", async (
                Guid employeeId, Guid extraId, ClaimsPrincipal user, IAuthorizationService auth, AbsenceService service, WorkforceUnitOfWork uow, CancellationToken ct) =>
            {
                if (!await Allows(auth, user, new EmployeeResource(employeeId), AbsencePrivileges.ManageEntitlement))
                {
                    return Results.Forbid();
                }

                var error = await uow.RunAsync(async () => AbsenceResult.Fail(await service.RemoveExtraVacationAsync(employeeId, extraId, ct)), r => r.Error == AbsenceError.None, ct);
                return error.Error == AbsenceError.None ? Results.NoContent() : Problem(error.Error);
            })
            .RequireAuthorization();

        // Stores are closed on public holidays unless marked otherwise (railway-station stores); the district manager maintains it.
        api.MapGet("/stores/{storeId:guid}/holiday-opening", async (Guid storeId, ClaimsPrincipal user, IAuthorizationService auth, WorkforceDbContext db, CancellationToken ct) =>
            {
                if (!await Allows(auth, user, new StoreResource(storeId), AbsencePrivileges.ManageHolidayOpening))
                {
                    return Results.Forbid();
                }

                if (!await db.Stores.AsNoTracking().AnyAsync(s => s.StoreId == storeId, ct))
                {
                    return Results.NotFound();
                }

                var row = await db.StoreSettings.AsNoTracking().FirstOrDefaultAsync(s => s.StoreId == storeId, ct);
                return Results.Ok(new StoreHolidayOpeningDto(storeId, row?.OpensOnHolidays ?? false, row?.ChangedAt, row?.ChangedBy));
            })
            .RequireAuthorization();

        // Many stores at once, so that nobody has to maintain every railway-station store by hand. All or nothing: one store outside the reach refuses all.
        api.MapPut("/stores/holiday-opening", async (
                SetStoresHolidayOpeningRequest request, ClaimsPrincipal user, IAuthorizationService auth, WorkforceDbContext db, TimeProvider time, WorkforceUnitOfWork uow, CancellationToken ct) =>
            {
                if (Me(user) is not { } me)
                {
                    return Results.Forbid();
                }

                var ids = request.StoreIds.Distinct().ToList();
                if (ids.Count == 0 || ids.Count > 500)
                {
                    return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: nameof(AbsenceError.InvalidPeriod));
                }

                foreach (var id in ids)
                {
                    if (!await Allows(auth, user, new StoreResource(id), AbsencePrivileges.ManageHolidayOpening))
                    {
                        return Results.Forbid();
                    }
                }

                if (await db.Stores.AsNoTracking().CountAsync(s => ids.Contains(s.StoreId), ct) != ids.Count)
                {
                    return Results.NotFound();
                }

                await uow.RunAsync(async () =>
                {
                    var rows = await db.StoreSettings.Where(s => ids.Contains(s.StoreId)).ToDictionaryAsync(s => s.StoreId, ct);
                    var now = time.GetUtcNow();
                    foreach (var id in ids)
                    {
                        if (!rows.TryGetValue(id, out var row))
                        {
                            row = new StoreSettingsRecord { StoreId = id };
                            db.StoreSettings.Add(row);
                        }

                        row.OpensOnHolidays = request.OpensOnHolidays;
                        row.ChangedBy = me;
                        row.ChangedAt = now;
                    }

                    return true;
                }, ok => ok, ct);
                return Results.NoContent();
            })
            .RequireAuthorization();

        api.MapPut("/stores/{storeId:guid}/holiday-opening", async (
                Guid storeId, SetStoreHolidayOpeningRequest request, ClaimsPrincipal user, IAuthorizationService auth, WorkforceDbContext db, TimeProvider time, WorkforceUnitOfWork uow, CancellationToken ct) =>
            {
                if (Me(user) is not { } me || !await Allows(auth, user, new StoreResource(storeId), AbsencePrivileges.ManageHolidayOpening))
                {
                    return Results.Forbid();
                }

                if (!await db.Stores.AsNoTracking().AnyAsync(s => s.StoreId == storeId, ct))
                {
                    return Results.NotFound();
                }

                await uow.RunAsync(async () =>
                {
                    var row = await db.StoreSettings.FindAsync([storeId], ct);
                    if (row is null)
                    {
                        row = new StoreSettingsRecord { StoreId = storeId };
                        db.StoreSettings.Add(row);
                    }

                    row.OpensOnHolidays = request.OpensOnHolidays;
                    row.ChangedBy = me;
                    row.ChangedAt = time.GetUtcNow();
                    return true;
                }, ok => ok, ct);
                return Results.NoContent();
            })
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
        await service.BreakdownAsync(employeeId, year ?? service.TodayLocal.Year, ct) is { } b
            ? Results.Ok(new VacationBalanceDto(
                b.Year, b.EffectiveDays, b.Taken, b.Planned, b.Remaining, b.ContractualAnnualDays, b.ContractualDays, b.ExtraDays, b.ForfeitedExtraDays,
                [.. b.Lines.Select(l => new ExtraDaysLineDto(l.Id, l.Kind.ToString(), l.Year, l.Days, l.AvailableInYear, l.UsedInYear, l.Forfeited, l.ExpiresOn))]))
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
        result.Error == AbsenceError.None ? Results.Ok(ToDto(result.Absence!, reason: true, sync: false, certificate: true)) : Problem(result.Error);

    internal static IResult Problem(AbsenceError error) => Results.Problem(
        statusCode: error switch
        {
            AbsenceError.NotFound or AbsenceError.UnknownPerson => StatusCodes.Status404NotFound,
            AbsenceError.NotAllowed => StatusCodes.Status403Forbidden,
            AbsenceError.FileTooLarge => StatusCodes.Status413PayloadTooLarge,
            AbsenceError.UnsupportedFileType => StatusCodes.Status415UnsupportedMediaType,
            AbsenceError.InvalidPeriod or AbsenceError.AfterExit or AbsenceError.NoEntitlement or AbsenceError.InsufficientBalance or AbsenceError.FileRejected or AbsenceError.InvalidRetention or AbsenceError.InvalidReminderSettings or AbsenceError.InvalidVacationRules or AbsenceError.WaitingPeriod => StatusCodes.Status422UnprocessableEntity,
            _ => StatusCodes.Status409Conflict,
        },
        title: error.ToString());

    private static AbsenceDto ToDto(Absence a, bool reason, bool sync, bool certificate) => new(
        a.Id, a.EmployeeId, reason ? a.Type.ToString() : null, a.Status.ToString(), a.From, a.To,
        reason && certificate && a.Type == AbsenceType.Sickness ? a.CertificateStatus.ToString() : null, reason && certificate ? a.CertifiedThrough : null,
        sync ? a.Version : null, sync ? a.SyncStatus.ToString() : null);

    internal static async Task<bool> Allows(IAuthorizationService auth, ClaimsPrincipal user, object resource, string privilege) =>
        (await auth.AuthorizeAsync(user, resource, AppPolicies.Privilege(privilege))).Succeeded;

    internal static Guid? Me(ClaimsPrincipal user) => Guid.TryParse(user.FindFirst(AppClaimTypes.EmployeeId)?.Value, out var id) ? id : null;
}
