using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Shared.Authorization;
using Workforce.Core.WorkingTime;
using Workforce.Infrastructure.Absences;

namespace Workforce.Api;

/// <summary>
/// The working time account. Reading is class 2 data: the person themself (<see cref="WorkingTimePrivileges.ReadOwn"/>) and holders of
/// <see cref="WorkingTimePrivileges.Read"/> for that person. Booking and the rules are HR's. Another person's account is refused (403), not hidden.
/// </summary>
public static class WorkingTimeEndpoints
{
    public static WebApplication MapWorkingTimeEndpoints(this WebApplication app)
    {
        app.MapGet("/api/me/working-time", async (ClaimsPrincipal user, WorkingTimeService service, CancellationToken ct) =>
                Endpoints.Me(user) is not { } me ? Results.Forbid() : await View(service, me, ct))
            .RequireAuthorization(AppPolicies.Privilege(WorkingTimePrivileges.ReadOwn));

        app.MapGet("/api/employees/{employeeId:guid}/working-time", async (Guid employeeId, ClaimsPrincipal user, IAuthorizationService auth, WorkingTimeService service, CancellationToken ct) =>
                !await Endpoints.Allows(auth, user, new EmployeeResource(employeeId), WorkingTimePrivileges.Read) ? Results.Forbid() : await View(service, employeeId, ct))
            .RequireAuthorization();

        Book(app, "opening-balance", (s, id, r, actor, ct) => s.SetOpeningBalanceAsync(id, r.Hours, r.Date, actor, ct));
        Book(app, "corrections", (s, id, r, actor, ct) => s.CorrectAsync(id, r.Hours, r.Date, actor, ct));
        Book(app, "time-off", (s, id, r, actor, ct) => s.BookTimeOffAsync(id, r.Hours, r.Date, actor, ct));
        Book(app, "payouts", (s, id, r, actor, ct) => s.PayoutAsync(id, r.Hours, r.Date, actor, ct));

        app.MapPost("/api/employees/{employeeId:guid}/working-time/payout-at-exit", async (
                Guid employeeId, ClaimsPrincipal user, IAuthorizationService auth, WorkingTimeService service, WorkforceUnitOfWork uow, CancellationToken ct) =>
            {
                if (Endpoints.Me(user) is not { } actor || !await Endpoints.Allows(auth, user, new EmployeeResource(employeeId), WorkingTimePrivileges.Book))
                {
                    return Results.Forbid();
                }

                var result = await uow.RunAsync(() => service.PayoutAtExitAsync(employeeId, actor, ct), r => r.Error == WorkingTimeError.None, ct);
                return result.Error == WorkingTimeError.None ? Results.Ok(ToDto(result.Booking!)) : Problem(result.Error);
            })
            .RequireAuthorization();

        app.MapPost("/api/employees/{employeeId:guid}/working-time/month-closes", async (
                Guid employeeId, CloseMonthRequest request, ClaimsPrincipal user, IAuthorizationService auth, WorkingTimeService service, WorkforceUnitOfWork uow, CancellationToken ct) =>
            {
                if (Endpoints.Me(user) is not { } actor || !await Endpoints.Allows(auth, user, new EmployeeResource(employeeId), WorkingTimePrivileges.Book))
                {
                    return Results.Forbid();
                }

                var result = await uow.RunAsync(() => service.CloseMonthAsync(employeeId, request.Year, request.Month, request.WorkedHours, actor, ct), r => r.Error == WorkingTimeError.None, ct);
                return result.Error == WorkingTimeError.None ? Results.Ok(new MonthCloseResultDto(ToDto(result.Close!), result.Booking is null ? null : ToDto(result.Booking))) : Problem(result.Error);
            })
            .RequireAuthorization();

        MapRules(app);
        return app;
    }

    private static void MapRules(WebApplication app)
    {
        app.MapGet("/api/working-time-rules", async (WorkingTimeService service, CancellationToken ct) => Results.Ok(ToDto(await service.GetRulesAsync(ct))))
            .RequireAuthorization(AppPolicies.Privilege(WorkingTimePrivileges.ManageRules));

        app.MapPut("/api/working-time-rules", async (SetWorkingTimeRulesRequest request, ClaimsPrincipal user, WorkingTimeService service, WorkforceUnitOfWork uow, CancellationToken ct) =>
            {
                if (Endpoints.Me(user) is not { } actor)
                {
                    return Results.Forbid();
                }

                var error = await uow.RunAsync(() => service.SetRulesAsync(new WorkingTimeRules(request.PayoutThresholdHours ?? 0, request.ExpiryMonths, request.BookMinusHours ?? false, request.MaxMinusHours), actor, ct), e => e == WorkingTimeError.None, ct);
                return error == WorkingTimeError.None ? Results.NoContent() : Problem(error);
            })
            .RequireAuthorization(AppPolicies.Privilege(WorkingTimePrivileges.ManageRules));

        app.MapGet("/api/working-time-thresholds", async (WorkingTimeService service, CancellationToken ct) =>
                Results.Ok((await service.ListThresholdsAsync(ct)).Select(t => new MonthlyThresholdDto(t.RelationshipId, t.Hours))))
            .RequireAuthorization(AppPolicies.Privilege(WorkingTimePrivileges.ManageRules));

        // The monthly threshold of an employment relationship (the id from /api/employee-lists/EmploymentRelationship); hours left out removes it.
        app.MapPut("/api/working-time-thresholds/{relationshipId:guid}", async (
                Guid relationshipId, SetThresholdRequest request, ClaimsPrincipal user, WorkingTimeService service, WorkforceUnitOfWork uow, CancellationToken ct) =>
            {
                if (Endpoints.Me(user) is not { } actor)
                {
                    return Results.Forbid();
                }

                var error = await uow.RunAsync(() => service.SetThresholdAsync(relationshipId, request.Hours, actor, ct), e => e == WorkingTimeError.None, ct);
                return error == WorkingTimeError.None ? Results.NoContent() : Problem(error);
            })
            .RequireAuthorization(AppPolicies.Privilege(WorkingTimePrivileges.ManageRules));
    }

    private static void Book(WebApplication app, string path, Func<WorkingTimeService, Guid, BookHoursRequest, Guid, CancellationToken, Task<BookingResult>> action) =>
        app.MapPost($"/api/employees/{{employeeId:guid}}/working-time/{path}", async (
                Guid employeeId, BookHoursRequest request, ClaimsPrincipal user, IAuthorizationService auth, WorkingTimeService service, WorkforceUnitOfWork uow, CancellationToken ct) =>
            {
                if (Endpoints.Me(user) is not { } actor || !await Endpoints.Allows(auth, user, new EmployeeResource(employeeId), WorkingTimePrivileges.Book))
                {
                    return Results.Forbid();
                }

                var result = await uow.RunAsync(() => action(service, employeeId, request, actor, ct), r => r.Error == WorkingTimeError.None, ct);
                return result.Error == WorkingTimeError.None ? Results.Ok(ToDto(result.Booking!)) : Problem(result.Error);
            })
            .RequireAuthorization();

    private static async Task<IResult> View(WorkingTimeService service, Guid employeeId, CancellationToken ct) =>
        await service.GetAsync(employeeId, ct) is { } view
            ? Results.Ok(new WorkingTimeDto(
                view.State.Balance, view.State.Deficit, view.State.Forfeited,
                [.. view.State.Buckets.Select(b => new AccountBucketDto(b.EarnedOn, b.Remaining, b.ExpiresOn))],
                [.. view.Bookings.Select(ToDto)], [.. view.Closes.Select(ToDto)],
                new WorkingTimeRulesDto(view.Rules.PayoutThresholdHours, view.Rules.ExpiryMonths, view.Rules == WorkingTimeRules.Default, null, null, view.Rules.BookMinusHours, view.Rules.MaxMinusHours)))
            : Problem(WorkingTimeError.UnknownPerson);

    private static WorkingTimeRulesDto ToDto((WorkingTimeRules Rules, bool IsDefault, DateTimeOffset? ChangedAt, Guid? ChangedBy) r) =>
        new(r.Rules.PayoutThresholdHours, r.Rules.ExpiryMonths, r.IsDefault, r.ChangedAt, r.ChangedBy, r.Rules.BookMinusHours, r.Rules.MaxMinusHours);

    private static TimeBookingDto ToDto(TimeBooking b) => new(b.Id, b.Kind.ToString(), b.Hours, b.Date, b.MonthCloseId, b.CreatedAt);

    private static MonthCloseDto ToDto(MonthClose c) => new(c.Id, c.Year, c.Month, c.WorkedHours, c.ThresholdHours, c.OvertimeHours, c.ClosedAt, c.MinusHours, c.MinusHoursSkipped);

    private static IResult Problem(WorkingTimeError error) => Results.Problem(
        statusCode: error switch
        {
            WorkingTimeError.UnknownPerson or WorkingTimeError.NotFound or WorkingTimeError.UnknownRelationship => StatusCodes.Status404NotFound,
            WorkingTimeError.OpeningBalanceExists or WorkingTimeError.AlreadyClosed or WorkingTimeError.InsufficientBalance or WorkingTimeError.BelowPayoutThreshold
                or WorkingTimeError.NothingToPay or WorkingTimeError.NotLeaving or WorkingTimeError.NoThreshold or WorkingTimeError.NoEmploymentRelationship or WorkingTimeError.MonthNotFinished => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status422UnprocessableEntity,
        },
        title: error.ToString());
}
