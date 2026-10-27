using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Shared.Authorization;
using Workforce.Core.Employees;
using Workforce.Infrastructure.Absences;

namespace Workforce.Api;

/// <summary>
/// The consolidated employee card: creating new employees (the personnel number comes from the number series), changing the card, the
/// selection lists and the number series. Who sees what: the card without private data and wage type needs
/// <see cref="EmployeePrivileges.Read"/>; private data needs <see cref="EmployeePrivileges.ReadPrivate"/> or is the person's own;
/// the wage type needs <see cref="EmployeePrivileges.ReadWageType"/> and nobody else sees it, not even the person.
/// </summary>
public static class EmployeeEndpoints
{
    public static WebApplication MapEmployeeEndpoints(this WebApplication app)
    {
        MapCards(app);
        MapLists(app);
        return app;
    }

    private static void MapCards(WebApplication app)
    {
        // A new employee: the number is assigned here. Scoped privileges need a store in reach; without a store (head office) the privilege must be unrestricted.
        app.MapPost("/api/employees", async (
                EmployeeRequest request, ClaimsPrincipal user, IAuthorizationService auth, IPermissionCache cache, EmployeeService service, WorkforceUnitOfWork uow, CancellationToken ct) =>
            {
                if (Endpoints.Me(user) is not { } me)
                {
                    return Results.Forbid();
                }

                if (!await MayAsync(auth, cache, user, me, request.MainStoreId, EmployeePrivileges.Create)
                    || (request.WageTypeId is not null && !await MayAsync(auth, cache, user, me, request.MainStoreId, EmployeePrivileges.ReadWageType)))
                {
                    return Results.Forbid();
                }

                if (!Enum.TryParse<Gender>(request.Gender, ignoreCase: true, out var gender))
                {
                    return Problem(EmployeeError.Invalid);
                }

                var result = await uow.RunAsync(() => service.CreateAsync(request.ToData(gender), request.VacationDaysPerYear, me, ct), r => r.Error == EmployeeError.None, ct);
                return result.Error != EmployeeError.None
                    ? Problem(result.Error)
                    : Results.Ok(new CreatedEmployeeDto(result.Card!.EmployeeId, result.Card.PersonnelNumber));
            })
            .RequireAuthorization();

        app.MapGet("/api/employees/{employeeId:guid}/card", async (
                Guid employeeId, ClaimsPrincipal user, IAuthorizationService auth, EmployeeService service, CancellationToken ct) =>
            {
                var self = Endpoints.Me(user) == employeeId && await Endpoints.Allows(auth, user, new object(), EmployeePrivileges.ReadOwn);
                if (!self && !await Endpoints.Allows(auth, user, new EmployeeResource(employeeId), EmployeePrivileges.Read))
                {
                    return Results.Forbid();
                }

                if (await service.GetAsync(employeeId, ct) is not { } card)
                {
                    return Results.NotFound();
                }

                var includePrivate = self || await Endpoints.Allows(auth, user, new EmployeeResource(employeeId), EmployeePrivileges.ReadPrivate);
                var includeWage = await Endpoints.Allows(auth, user, new EmployeeResource(employeeId), EmployeePrivileges.ReadWageType);
                return Results.Ok(ToDto(card, includePrivate, includeWage));
            })
            .RequireAuthorization();

        // The own card, with the private data (that the person may see) but never the wage type.
        app.MapGet("/api/me/employee-card", async (ClaimsPrincipal user, EmployeeService service, CancellationToken ct) =>
                Endpoints.Me(user) is not { } me || await service.GetAsync(me, ct) is not { } card
                    ? Results.NotFound()
                    : Results.Ok(ToDto(card, includePrivate: true, includeWage: false)))
            .RequireAuthorization(AppPolicies.Privilege(EmployeePrivileges.ReadOwn));

        app.MapPut("/api/employees/{employeeId:guid}/card", async (
                Guid employeeId, EmployeeRequest request, ClaimsPrincipal user, IAuthorizationService auth, EmployeeService service, WorkforceUnitOfWork uow, CancellationToken ct) =>
            {
                if (Endpoints.Me(user) is not { } me || !await Endpoints.Allows(auth, user, new EmployeeResource(employeeId), EmployeePrivileges.Update))
                {
                    return Results.Forbid();
                }

                if (!Enum.TryParse<Gender>(request.Gender, ignoreCase: true, out var gender))
                {
                    return Problem(EmployeeError.Invalid);
                }

                var mayChangeWageType = await Endpoints.Allows(auth, user, new EmployeeResource(employeeId), EmployeePrivileges.ReadWageType);
                var error = await uow.RunAsync(async () => await service.UpdateAsync(employeeId, request.ToData(gender), mayChangeWageType, me, ct), e => e == EmployeeError.None, ct);
                return error == EmployeeError.None ? Results.NoContent() : Problem(error);
            })
            .RequireAuthorization();

        app.MapGet("/api/personnel-number-series", async (EmployeeService service, CancellationToken ct) =>
                Results.Ok(new NumberSeriesDto(await service.LastNumberAsync(ct))))
            .RequireAuthorization(AppPolicies.Privilege(EmployeePrivileges.ManageNumberSeries));

        // HR enters the last personnel number used so far; the next new employee gets one more.
        app.MapPut("/api/personnel-number-series", async (SetNumberSeriesRequest request, ClaimsPrincipal user, EmployeeService service, WorkforceUnitOfWork uow, CancellationToken ct) =>
            {
                if (Endpoints.Me(user) is not { } me)
                {
                    return Results.Forbid();
                }

                var error = await uow.RunAsync(async () => await service.SetLastNumberAsync(request.LastNumber, me, ct), e => e == EmployeeError.None, ct);
                return error == EmployeeError.None ? Results.Ok(new NumberSeriesDto(request.LastNumber)) : Problem(error);
            })
            .RequireAuthorization(AppPolicies.Privilege(EmployeePrivileges.ManageNumberSeries));
    }

    private static void MapLists(WebApplication app)
    {
        // The selection lists (function, role, region, ...): readable for everybody who creates or changes cards or maintains the lists.
        app.MapGet("/api/employee-lists/{kind}", async (string kind, ClaimsPrincipal user, IAuthorizationService auth, EmployeeService service, CancellationToken ct) =>
            {
                if (!await Endpoints.Allows(auth, user, new object(), EmployeePrivileges.ManageLists)
                    && !await Endpoints.Allows(auth, user, new object(), EmployeePrivileges.Create)
                    && !await Endpoints.Allows(auth, user, new object(), EmployeePrivileges.Update))
                {
                    return Results.Forbid();
                }

                return ParseKind(kind) is not { } parsed
                    ? Results.NotFound()
                    : Results.Ok((await service.ListAsync(parsed, ct)).Select(ToDto));
            })
            .RequireAuthorization();

        app.MapPost("/api/employee-lists/{kind}", async (string kind, AddReferenceEntryRequest request, EmployeeService service, WorkforceUnitOfWork uow, CancellationToken ct) =>
            {
                if (ParseKind(kind) is not { } parsed)
                {
                    return Results.NotFound();
                }

                var result = await uow.RunAsync(() => service.AddEntryAsync(parsed, request.Code, request.Name, ct), r => r.Error == EmployeeError.None, ct);
                return result.Error == EmployeeError.None ? Results.Ok(ToDto(result.Entry!)) : Problem(result.Error);
            })
            .RequireAuthorization(AppPolicies.Privilege(EmployeePrivileges.ManageLists));

        app.MapPut("/api/employee-lists/{kind}/{entryId:guid}", async (string kind, Guid entryId, UpdateReferenceEntryRequest request, EmployeeService service, WorkforceUnitOfWork uow, CancellationToken ct) =>
            {
                if (ParseKind(kind) is null)
                {
                    return Results.NotFound();
                }

                var error = await uow.RunAsync(async () => await service.UpdateEntryAsync(entryId, request.Name, request.IsActive, ct), e => e == EmployeeError.None, ct);
                return error == EmployeeError.None ? Results.NoContent() : Problem(error);
            })
            .RequireAuthorization(AppPolicies.Privilege(EmployeePrivileges.ManageLists));
    }

    private static ReferenceKind? ParseKind(string kind) =>
        Enum.TryParse<ReferenceKind>(kind, ignoreCase: true, out var parsed) && parsed != ReferenceKind.Unknown && Enum.IsDefined(parsed) ? parsed : null;

    /// <summary>With a store: the privilege must reach it. Without (head office): the privilege must be unrestricted.</summary>
    private static async Task<bool> MayAsync(IAuthorizationService auth, IPermissionCache cache, ClaimsPrincipal user, Guid me, Guid? storeId, string privilege) =>
        storeId is { } store
            ? await Endpoints.Allows(auth, user, new StoreResource(store), privilege)
            : cache.TryGet(me, out var snapshot) && snapshot is not null && snapshot.Privileges.TryGetValue(privilege, out var reach) && reach.Unrestricted;

    private static EmployeeCardDto ToDto(EmployeeCard c, bool includePrivate, bool includeWage) => new(
        c.EmployeeId, c.PersonnelNumber, c.FirstName, c.LastName, c.ReceiptName, c.BusinessEmail, c.MainStoreId, c.FunctionId, c.RoleId, c.RegionId, c.WorkplaceId,
        c.EmploymentRelationshipId, c.EmploymentCodeId, c.AuthorizationGroupId, c.HireDate, c.ExitDate, c.SupervisorId, c.QtradoExport, c.WebTimeTrackingAllowed,
        c.PasswordChangeRequired, c.Note, c.State.ToString(),
        includePrivate ? new EmployeePrivateDto(c.Gender.ToString(), c.BirthDate, c.Street, c.PostalCode, c.City, c.PrivatePhone, c.PrivateEmail) : null,
        includeWage ? new EmployeeWageDto(c.WageTypeId) : null);

    private static ReferenceEntryDto ToDto(ReferenceEntry e) => new(e.Id, e.Kind.ToString(), e.Code, e.Name, e.IsActive);

    private static IResult Problem(EmployeeError error) => Results.Problem(
        statusCode: error switch
        {
            EmployeeError.NotFound => StatusCodes.Status404NotFound,
            EmployeeError.NotAllowed => StatusCodes.Status403Forbidden,
            EmployeeError.NumberSeriesNotSet or EmployeeError.DuplicateNumber or EmployeeError.DuplicateCode => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status422UnprocessableEntity,
        },
        title: error.ToString());
}
