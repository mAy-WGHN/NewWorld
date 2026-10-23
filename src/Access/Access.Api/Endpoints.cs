using System.Security.Claims;
using Access.Core;
using Access.Core.Acl;
using Access.Core.Publishing;
using Access.Core.Responsibilities;
using Access.Infrastructure;
using Access.Infrastructure.Acl;
using Microsoft.EntityFrameworkCore;
using Shared.Authorization;

namespace Access.Api;

/// <summary>
/// Endpoints of the Access service. Frontends: own privileges and the administration of the ACL (each call needs the privilege and
/// passes the protection rules). Services: catalogue registration and the complete current state for rebuilding a copy.
/// </summary>
public static class Endpoints
{
    public static WebApplication MapAccessEndpoints(this WebApplication app)
    {
        app.MapAuthEndpoints();
        var api = app.MapGroup("/api");

        // Decision 5: privileges and reach of the signed-in person, computed fresh.
        api.MapGet("/me/privileges", async (ClaimsPrincipal user, IAclReader reader, TimeProvider time, AccessOptions options, CancellationToken ct) =>
            Actor(user) is not { } me
                ? Results.Forbid()
                : Results.Ok(ToDto(me, EffectivePermissionCalculator.Calculate(me, await reader.LoadAsync(ct), time.GetUtcNow(), options.Zone))))
            .RequireAuthorization();

        api.MapGet("/catalog", async (AccessDbContext db, CancellationToken ct) =>
                Results.Ok(await db.Catalog.OrderBy(c => c.Key).Select(c => new CatalogEntryDto(c.Key, c.Owner, c.Scopable)).ToListAsync(ct)))
            .RequireAuthorization(AppPolicies.Privilege(AccessPrivileges.CatalogRead));

        MapRoles(api);
        MapScopes(api);
        MapAssignments(api);
        MapResponsibilities(api);

        api.MapPost("/custom-privileges", (CreateCustomPrivilegeRequest request, ClaimsPrincipal user, AclAdministration admin, CancellationToken ct) =>
                Run(user, actor => admin.CreateCustomPrivilegeAsync(actor, request.Key, request.Scopable, ct), _ => Results.NoContent()))
            .RequireAuthorization(AppPolicies.Privilege(AccessPrivileges.CustomPrivilegeCreate));
        api.MapDelete("/custom-privileges/{key}", (string key, ClaimsPrincipal user, AclAdministration admin, CancellationToken ct) =>
                Run(user, actor => admin.DeleteCustomPrivilegeAsync(actor, key, ct), _ => Results.NoContent()))
            .RequireAuthorization(AppPolicies.Privilege(AccessPrivileges.CustomPrivilegeDelete));

        MapInternal(app);
        return app;
    }

    private static void MapRoles(RouteGroupBuilder api)
    {
        api.MapGet("/roles", async (AccessDbContext db, CancellationToken ct) =>
                Results.Ok((await db.Roles.OrderBy(r => r.Name).ToListAsync(ct)).Select(r => new RoleDto(r.Id, r.Name, r.Privileges, r.BuiltIn))))
            .RequireAuthorization(AppPolicies.Privilege(AccessPrivileges.RoleRead));
        api.MapPost("/roles", (CreateRoleRequest request, ClaimsPrincipal user, AclAdministration admin, CancellationToken ct) =>
                Run(user, actor => admin.CreateRoleAsync(actor, request.Name, request.Privileges.ToHashSet(StringComparer.Ordinal), ct), Created))
            .RequireAuthorization(AppPolicies.Privilege(AccessPrivileges.RoleCreate));
        api.MapPut("/roles/{roleId:guid}", (Guid roleId, UpdateRoleRequest request, ClaimsPrincipal user, AclAdministration admin, CancellationToken ct) =>
                Run(user, actor => admin.UpdateRoleAsync(actor, roleId, request.Privileges.ToHashSet(StringComparer.Ordinal), ct), _ => Results.NoContent()))
            .RequireAuthorization(AppPolicies.Privilege(AccessPrivileges.RoleUpdate));
        api.MapDelete("/roles/{roleId:guid}", (Guid roleId, ClaimsPrincipal user, AclAdministration admin, CancellationToken ct) =>
                Run(user, actor => admin.DeleteRoleAsync(actor, roleId, ct), _ => Results.NoContent()))
            .RequireAuthorization(AppPolicies.Privilege(AccessPrivileges.RoleDelete));
    }

    private static void MapScopes(RouteGroupBuilder api)
    {
        api.MapGet("/scopes", async (AccessDbContext db, CancellationToken ct) =>
                Results.Ok((await db.Scopes.OrderBy(s => s.Name).ToListAsync(ct)).Select(s => new ScopeDto(s.Id, s.Name, s.StoreIds))))
            .RequireAuthorization(AppPolicies.Privilege(AccessPrivileges.ScopeRead));
        api.MapPost("/scopes", (CreateScopeRequest request, ClaimsPrincipal user, AclAdministration admin, CancellationToken ct) =>
                Run(user, actor => admin.CreateScopeAsync(actor, request.Name, request.StoreIds.ToHashSet(), ct), Created))
            .RequireAuthorization(AppPolicies.Privilege(AccessPrivileges.ScopeCreate));
        api.MapPut("/scopes/{scopeId:guid}", (Guid scopeId, UpdateScopeRequest request, ClaimsPrincipal user, AclAdministration admin, CancellationToken ct) =>
                Run(user, actor => admin.UpdateScopeAsync(actor, scopeId, request.StoreIds.ToHashSet(), ct), _ => Results.NoContent()))
            .RequireAuthorization(AppPolicies.Privilege(AccessPrivileges.ScopeUpdate));
        api.MapDelete("/scopes/{scopeId:guid}", (Guid scopeId, ClaimsPrincipal user, AclAdministration admin, CancellationToken ct) =>
                Run(user, actor => admin.DeleteScopeAsync(actor, scopeId, ct), _ => Results.NoContent()))
            .RequireAuthorization(AppPolicies.Privilege(AccessPrivileges.ScopeDelete));
    }

    private static void MapAssignments(RouteGroupBuilder api)
    {
        api.MapGet("/people/{employeeId:guid}/assignments", async (Guid employeeId, ClaimsPrincipal user, IAuthorizationServiceAdapter auth, AccessDbContext db, CancellationToken ct) =>
                !await auth.AllowsAsync(user, new EmployeeResource(employeeId), AccessPrivileges.AssignmentRead)
                    ? Results.Forbid()
                    : Results.Ok((await db.Assignments.Where(a => a.EmployeeId == employeeId).ToListAsync(ct)).Select(a => new AssignmentDto(
                        a.Id, a.EmployeeId, a.RoleId, a.Unrestricted, a.ScopeIds, a.IncludeHomeStore, a.ValidFrom, a.ValidTo, a.Source))))
            .RequireAuthorization(AppPolicies.Privilege(AccessPrivileges.AssignmentRead));
        api.MapPost("/people/{employeeId:guid}/assignments", (Guid employeeId, CreateAssignmentRequest request, ClaimsPrincipal user, AclAdministration admin, CancellationToken ct) =>
                Run(user, actor => admin.AssignAsync(
                    actor, employeeId, request.RoleId,
                    request.Unrestricted ? AssignmentReach.Everywhere : AssignmentReach.Scopes(request.ScopeIds ?? [], request.IncludeHomeStore),
                    request.ValidFrom, request.ValidTo, ct), Created))
            .RequireAuthorization(AppPolicies.Privilege(AccessPrivileges.AssignmentCreate));
        api.MapDelete("/assignments/{assignmentId:guid}", (Guid assignmentId, ClaimsPrincipal user, AclAdministration admin, CancellationToken ct) =>
                Run(user, actor => admin.RemoveAssignmentAsync(actor, assignmentId, ct), _ => Results.NoContent()))
            .RequireAuthorization(AppPolicies.Privilege(AccessPrivileges.AssignmentDelete));
    }

    private static void MapResponsibilities(RouteGroupBuilder api)
    {
        api.MapGet("/people/{employeeId:guid}/responsibilities", async (
                Guid employeeId, ClaimsPrincipal user, IAuthorizationServiceAdapter auth, IAclReader reader, TimeProvider time, AccessOptions options, CancellationToken ct) =>
                !await auth.AllowsAsync(user, new EmployeeResource(employeeId), AccessPrivileges.ResponsibilityRead)
                    ? Results.Forbid()
                    : Results.Ok(ResponsibilityCalculator.Calculate(employeeId, await reader.LoadAsync(ct), time.GetUtcNow(), options.Zone)
                        .Select(r => new TopicResponsibilityDto(r.Key, r.Value))))
            .RequireAuthorization(AppPolicies.Privilege(AccessPrivileges.ResponsibilityRead));
        api.MapPost("/responsibilities", (CreateResponsibilityRequest request, ClaimsPrincipal user, AclAdministration admin, CancellationToken ct) =>
                ToTarget(request.Target) is not { } target
                    ? Task.FromResult(Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: nameof(GuardReason.Invalid)))
                    : Run(user, actor => admin.AddResponsibilityAsync(actor, request.Topic, request.ResponsibleEmployeeId, target, request.ValidFrom, request.ValidTo, ct), Created))
            .RequireAuthorization(AppPolicies.Privilege(AccessPrivileges.ResponsibilityCreate));
        api.MapDelete("/responsibilities/{responsibilityId:guid}", (Guid responsibilityId, ClaimsPrincipal user, AclAdministration admin, CancellationToken ct) =>
                Run(user, actor => admin.RemoveResponsibilityAsync(actor, responsibilityId, ct), _ => Results.NoContent()))
            .RequireAuthorization(AppPolicies.Privilege(AccessPrivileges.ResponsibilityDelete));
    }

    private static void MapInternal(WebApplication app)
    {
        var internalApi = app.MapGroup("/internal");

        internalApi.MapPut("/apps/{appKey}/catalog", async (string appKey, RegisterCatalogRequest request, AclAdministration admin, CancellationToken ct) =>
                ToResult(await admin.RegisterCatalogAsync(appKey, request.Privileges.Select(p => new CatalogEntry(p.Key, appKey, p.Scopable)).ToList(), ct), _ => Results.NoContent()))
            .RequireAuthorization(AppPolicies.ServiceRole(AccessRoles.CatalogRegister));

        internalApi.MapGet("/permissions", async (IAclReader reader, IPublicationStore publications, TimeProvider time, AccessOptions options, CancellationToken ct) =>
            {
                var data = await reader.LoadAsync(ct);
                var versions = await publications.LoadAllAsync(ct);
                return Results.Ok(data.People
                    .Where(p => versions.ContainsKey((p.EmployeeId, PublicationKind.Permissions)))
                    .Select(p => new PermissionStateDto(
                        p.EmployeeId,
                        versions[(p.EmployeeId, PublicationKind.Permissions)].Version,
                        ToDto(p.EmployeeId, EffectivePermissionCalculator.Calculate(p.EmployeeId, data, time.GetUtcNow(), options.Zone)).Privileges)));
            })
            .RequireAuthorization(AppPolicies.ServiceRole(AccessRoles.PermissionsRead));

        internalApi.MapGet("/responsibilities", async (IAclReader reader, IPublicationStore publications, TimeProvider time, AccessOptions options, CancellationToken ct) =>
            {
                var data = await reader.LoadAsync(ct);
                var versions = await publications.LoadAllAsync(ct);
                return Results.Ok(data.People
                    .Where(p => versions.ContainsKey((p.EmployeeId, PublicationKind.Responsibilities)))
                    .Select(p => new ResponsibilityStateDto(
                        p.EmployeeId,
                        versions[(p.EmployeeId, PublicationKind.Responsibilities)].Version,
                        ResponsibilityCalculator.Calculate(p.EmployeeId, data, time.GetUtcNow(), options.Zone).Select(r => new TopicResponsibilityDto(r.Key, r.Value)).ToList())));
            })
            .RequireAuthorization(AppPolicies.ServiceRole(AccessRoles.PermissionsRead));
    }

    private static Guid? Actor(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirst(AppClaimTypes.EmployeeId)?.Value, out var id) ? id : null;

    private static async Task<IResult> Run(ClaimsPrincipal user, Func<Guid, Task<AdminResult>> action, Func<AdminResult, IResult> onSuccess) =>
        Actor(user) is not { } actor ? Results.Forbid() : ToResult(await action(actor), onSuccess);

    private static IResult ToResult(AdminResult result, Func<AdminResult, IResult> onSuccess) => result.Guard.Reason switch
    {
        GuardReason.None => onSuccess(result),
        GuardReason.NotFound => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: result.Guard.Reason.ToString()),
        GuardReason.InUse => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: result.Guard.Reason.ToString()),
        GuardReason.Invalid or GuardReason.UnknownPrivilege => Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: result.Guard.Reason.ToString()),
        _ => Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: result.Guard.Reason.ToString()),
    };

    private static IResult Created(AdminResult result) => Results.Ok(new CreatedDto(result.Id!.Value));

    private static ResponsibilityTarget? ToTarget(ResponsibilityTargetDto dto) => dto.Kind switch
    {
        "employee" => ResponsibilityTarget.ForEmployee(dto.Id),
        "store" => ResponsibilityTarget.ForStore(dto.Id),
        "scope" => ResponsibilityTarget.ForScope(dto.Id),
        _ => null,
    };

    private static MyPrivilegesDto ToDto(Guid employeeId, EffectivePermissions permissions) => new(
        employeeId,
        permissions.Roles,
        permissions.Privileges.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => new PrivilegeDto(p.Key, p.Value.Unrestricted, p.Value.StoreIds.Order().ToList(), p.Value.EmployeeIds.Order().ToList()))
            .ToList());
}

/// <summary>Resource-based privilege check for endpoint handlers.</summary>
public interface IAuthorizationServiceAdapter
{
    Task<bool> AllowsAsync(ClaimsPrincipal user, object resource, string privilege);
}

public sealed class AuthorizationServiceAdapter(Microsoft.AspNetCore.Authorization.IAuthorizationService authorization) : IAuthorizationServiceAdapter
{
    public async Task<bool> AllowsAsync(ClaimsPrincipal user, object resource, string privilege) =>
        (await authorization.AuthorizeAsync(user, resource, AppPolicies.Privilege(privilege))).Succeeded;
}
