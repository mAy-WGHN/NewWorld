using System.Security.Claims;
using Access.Core;
using Access.Core.Acl;
using Access.Core.Publishing;
using Access.Core.Responsibilities;
using Access.Infrastructure;
using Access.Infrastructure.Acl;
using Access.Infrastructure.StepUp;
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

        // Decision 5: privileges and reach of the signed-in person, computed fresh. The name and the personnel number come from the copy of the
        // people (organisation data, class 1), so that a frontend can say who is signed in without a call to another service.
        api.MapGet("/me/privileges", async (ClaimsPrincipal user, IAclReader reader, AccessDbContext db, TimeProvider time, AccessOptions options, CancellationToken ct) =>
            {
                if (Actor(user) is not { } me)
                {
                    return Results.Forbid();
                }

                var person = await db.People.AsNoTracking().Where(p => p.EmployeeId == me).Select(p => new { p.Name, p.PersonnelNumber }).FirstOrDefaultAsync(ct);
                return Results.Ok(ToDto(me, EffectivePermissionCalculator.Calculate(me, await reader.LoadAsync(ct), time.GetUtcNow(), options.Zone), person?.Name, person?.PersonnelNumber));
            })
            .RequireAuthorization();

        api.MapGet("/catalog", async (AccessDbContext db, CancellationToken ct) =>
                Results.Ok(await db.Catalog.OrderBy(c => c.Key).Select(c => new CatalogEntryDto(c.Key, c.Owner, c.Scopable, c.Description)).ToListAsync(ct)))
            .RequireAuthorization(AppPolicies.Privilege(AccessPrivileges.CatalogRead));

        MapRoles(api);
        MapScopes(api);
        MapAssignments(api);
        MapResponsibilities(api);
        MapMainStore(api);
        MapEntraLink(api);
        MapPeople(api);

        api.MapPost("/custom-privileges", (CreateCustomPrivilegeRequest request, ClaimsPrincipal user, AclAdministration admin, CancellationToken ct) =>
                Run(user, actor => admin.CreateCustomPrivilegeAsync(actor, request.Key, request.Scopable, request.Description, ct), _ => Results.NoContent()))
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
        // Roles as a file for moving them between environments: the export is a plain document, the import compares it with the existing roles (diff)
        // and applies only when asked to, and only the selected items.
        api.MapGet("/role-definitions", async (RoleDefinitionService service, CancellationToken ct) => Results.Ok(await service.ExportAsync(ct)))
            .RequireAuthorization(AppPolicies.Privilege(AccessPrivileges.RoleRead));
        api.MapPost("/role-definitions/import", async (ImportRolesRequest request, bool? apply, ClaimsPrincipal user, RoleDefinitionService service, CancellationToken ct) =>
                Actor(user) is not { } actor ? Results.Forbid() : Results.Ok(await service.ImportAsync(actor, request.Document, apply == true, request.Selected, ct)))
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

    // Finding a person for the administration (assignments, responsibilities, main store) by name or personnel number instead of the id.
    private static readonly string[] PersonLookupPrivileges =
        [AccessPrivileges.PersonRead, AccessPrivileges.AssignmentRead, AccessPrivileges.ResponsibilityRead, AccessPrivileges.MainStoreUpdate, AccessPrivileges.EntraLinkManage];

    private const int MaxLookupTake = 50;
    private const int MaxLookupIds = 100;
    private const int MaxSearchWords = 5;

    /// <summary>
    /// People of the copy (organisation data, class 1), by <c>query</c> (every word has to be part of the name or the personnel number, no case,
    /// at least two characters) or by <c>ids</c> (comma separated, to show the names of known ids). Only people in the caller's reach for
    /// at least one of the privileges that need a person (person:read, assignment:read, responsibility:read, main-store:update); without
    /// any of them nothing is offered at all.
    /// </summary>
    private static void MapPeople(RouteGroupBuilder api) =>
        api.MapGet("/people", async (string? query, string? ids, int? take, ClaimsPrincipal user, IAuthorizationServiceAdapter auth, AccessDbContext db, CancellationToken ct) =>
            {
                var usable = new List<string>();
                foreach (var privilege in PersonLookupPrivileges)
                {
                    if (await auth.AllowsAsync(user, new object(), privilege))
                    {
                        usable.Add(privilege);
                    }
                }

                if (usable.Count == 0)
                {
                    return Results.Forbid();
                }

                IQueryable<Access.Infrastructure.People.PersonRecord> people = db.People.AsNoTracking();
                if (!string.IsNullOrWhiteSpace(ids))
                {
                    var wanted = ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(MaxLookupIds).Select(i => Guid.TryParse(i, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).Distinct().ToList();
                    people = people.Where(p => wanted.Contains(p.EmployeeId));
                }
                else
                {
                    var words = (query ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(MaxSearchWords).ToList();
                    if (words.Sum(w => w.Length) < 2)
                    {
                        return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "QueryTooShort");
                    }

                    foreach (var word in words)
                    {
                        var pattern = "%" + word.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
                        people = people.Where(p => EF.Functions.ILike(p.Name, pattern, "\\") || EF.Functions.ILike(p.PersonnelNumber, pattern, "\\"));
                    }
                }

                var limit = Math.Clamp(take ?? 20, 1, MaxLookupTake);
                var result = new List<PersonDto>();
                foreach (var person in await people.OrderBy(p => p.Name).ThenBy(p => p.PersonnelNumber).Take(MaxLookupTake * 4).ToListAsync(ct))
                {
                    foreach (var privilege in usable)
                    {
                        if (await auth.AllowsAsync(user, new EmployeeResource(person.EmployeeId), privilege))
                        {
                            result.Add(new PersonDto(person.EmployeeId, person.PersonnelNumber, person.Name, person.HomeStoreId, person.Position, person.ExitDate));
                            break;
                        }
                    }

                    if (result.Count >= limit)
                    {
                        break;
                    }
                }

                return Results.Ok(result);
            })
            .RequireAuthorization();

    // The main store (Hauptfiliale): BC's "Vorgabefiliale" is only a suggestion; what is maintained here wins.
    private static void MapMainStore(RouteGroupBuilder api)
    {
        api.MapGet("/people/{employeeId:guid}/main-store", async (Guid employeeId, ClaimsPrincipal user, IAuthorizationServiceAdapter auth, AccessDbContext db, CancellationToken ct) =>
            {
                if (!await auth.AllowsAsync(user, new EmployeeResource(employeeId), AccessPrivileges.PersonRead))
                {
                    return Results.Forbid();
                }

                var suggested = await db.People.AsNoTracking().Where(p => p.EmployeeId == employeeId).Select(p => new { p.HomeStoreId }).FirstOrDefaultAsync(ct);
                if (suggested is null)
                {
                    return Results.NotFound();
                }

                var manual = await db.MainStoreOverrides.AsNoTracking().Where(m => m.EmployeeId == employeeId).Select(m => (Guid?)m.StoreId).FirstOrDefaultAsync(ct);
                return Results.Ok(new MainStoreDto(manual ?? suggested.HomeStoreId, manual, suggested.HomeStoreId));
            })
            .RequireAuthorization(AppPolicies.Privilege(AccessPrivileges.PersonRead));
        api.MapPut("/people/{employeeId:guid}/main-store", (Guid employeeId, SetMainStoreRequest request, ClaimsPrincipal user, AclAdministration admin, CancellationToken ct) =>
                Run(user, actor => admin.SetMainStoreAsync(actor, employeeId, request.StoreId, ct), _ => Results.NoContent()))
            .RequireAuthorization(AppPolicies.Privilege(AccessPrivileges.MainStoreUpdate));
        api.MapDelete("/people/{employeeId:guid}/main-store", (Guid employeeId, ClaimsPrincipal user, AclAdministration admin, CancellationToken ct) =>
                Run(user, actor => admin.SetMainStoreAsync(actor, employeeId, null, ct), _ => Results.NoContent()))
            .RequireAuthorization(AppPolicies.Privilege(AccessPrivileges.MainStoreUpdate));
    }

    // The Microsoft account of a person for the second factor (HR maintains it; the account id is bound by the first step-up).
    private static void MapEntraLink(RouteGroupBuilder api)
    {
        api.MapGet("/people/{employeeId:guid}/entra-link", async (Guid employeeId, ClaimsPrincipal user, IAuthorizationServiceAdapter auth, AccessDbContext db, CancellationToken ct) =>
            {
                if (!await auth.AllowsAsync(user, new EmployeeResource(employeeId), AccessPrivileges.PersonRead))
                {
                    return Results.Forbid();
                }

                var link = await db.EntraLinks.AsNoTracking().Where(l => l.EmployeeId == employeeId)
                    .Select(l => new EntraLinkDto(l.UserPrincipalName, l.EntraObjectId != null, l.BoundAt)).FirstOrDefaultAsync(ct);
                return link is null ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "NoEntraLink") : Results.Ok(link);
            })
            .RequireAuthorization(AppPolicies.Privilege(AccessPrivileges.PersonRead));
        api.MapPut("/people/{employeeId:guid}/entra-link", (Guid employeeId, SetEntraLinkRequest request, ClaimsPrincipal user, EntraLinkAdministration admin, CancellationToken ct) =>
                Run(user, actor => admin.SetAsync(actor, employeeId, request.UserPrincipalName, ct), _ => Results.NoContent()))
            .RequireAuthorization(AppPolicies.Privilege(AccessPrivileges.EntraLinkManage));
        api.MapDelete("/people/{employeeId:guid}/entra-link", (Guid employeeId, ClaimsPrincipal user, EntraLinkAdministration admin, CancellationToken ct) =>
                Run(user, actor => admin.RemoveAsync(actor, employeeId, ct), _ => Results.NoContent()))
            .RequireAuthorization(AppPolicies.Privilege(AccessPrivileges.EntraLinkManage));
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
                ToResult(await admin.RegisterCatalogAsync(appKey, request.Privileges.Select(p => new CatalogEntry(p.Key, appKey, p.Scopable, p.Description ?? "")).ToList(), ct), _ => Results.NoContent()))
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

    private static MyPrivilegesDto ToDto(Guid employeeId, EffectivePermissions permissions, string? name = null, string? personnelNumber = null) => new(
        employeeId,
        permissions.Roles,
        permissions.Privileges.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => new PrivilegeDto(p.Key, p.Value.Unrestricted, p.Value.StoreIds.Order().ToList(), p.Value.EmployeeIds.Order().ToList()))
            .ToList(),
        name,
        personnelNumber);
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
