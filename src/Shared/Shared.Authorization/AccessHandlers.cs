using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Shared.Authorization;

/// <summary>"May the user access store X?" Fail closed: no resolved identity or no snapshot means no access.</summary>
public sealed class StoreAccessHandler(IPermissionCache cache) : AuthorizationHandler<StoreAccessRequirement, StoreResource>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, StoreAccessRequirement requirement, StoreResource resource)
    {
        if (AccessHandlerHelper.TryGetSnapshot(context.User, cache, out var snapshot) && snapshot!.StoreIds.Contains(resource.StoreId))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

/// <summary>"May the user access employee Y?" The person themself is always allowed.</summary>
public sealed class EmployeeAccessHandler(IPermissionCache cache) : AuthorizationHandler<EmployeeAccessRequirement, EmployeeResource>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, EmployeeAccessRequirement requirement, EmployeeResource resource)
    {
        if (AccessHandlerHelper.TryGetEmployeeId(context.User, out var self) && self == resource.EmployeeId)
        {
            context.Succeed(requirement);
        }
        else if (AccessHandlerHelper.TryGetSnapshot(context.User, cache, out var snapshot) && snapshot!.EmployeeIds.Contains(resource.EmployeeId))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

internal static class AccessHandlerHelper
{
    public static bool TryGetEmployeeId(ClaimsPrincipal user, out Guid employeeId)
    {
        employeeId = default;
        return user.FindFirst(AppClaimTypes.EmployeeId)?.Value is { } value && Guid.TryParse(value, out employeeId);
    }

    public static bool TryGetSnapshot(ClaimsPrincipal user, IPermissionCache cache, out PermissionSnapshot? snapshot)
    {
        snapshot = null;
        return TryGetEmployeeId(user, out var id) && cache.TryGet(id, out snapshot);
    }
}
