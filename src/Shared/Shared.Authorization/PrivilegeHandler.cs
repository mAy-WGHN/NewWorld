using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Shared.Authorization;

/// <summary>
/// Checks a privilege against the local permission copy. Fail closed: no signed-in person, no copy or no privilege
/// means no access. A person is not implicitly in their own reach; access to own data is a privilege of its own.
/// </summary>
public sealed class PrivilegeHandler(IPermissionCache cache) : AuthorizationHandler<PrivilegeRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PrivilegeRequirement requirement)
    {
        if (TryGetSnapshot(context.User, out var snapshot))
        {
            var granted = context.Resource switch
            {
                StoreResource store => snapshot.AppliesToStore(requirement.Key, store.StoreId),
                EmployeeResource employee => snapshot.AppliesToEmployee(requirement.Key, employee.EmployeeId),
                _ => snapshot.Has(requirement.Key),
            };
            if (granted)
            {
                context.Succeed(requirement);
            }
        }

        return Task.CompletedTask;
    }

    private bool TryGetSnapshot(ClaimsPrincipal user, [NotNullWhen(true)] out PermissionSnapshot? snapshot)
    {
        snapshot = null;
        return user.FindFirst(AppClaimTypes.EmployeeId)?.Value is { } value
               && Guid.TryParse(value, out var employeeId)
               && cache.TryGet(employeeId, out snapshot)
               && snapshot is not null;
    }
}
