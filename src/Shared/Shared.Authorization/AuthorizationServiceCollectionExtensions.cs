using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Shared.Authorization;

public static class AuthorizationServiceCollectionExtensions
{
    /// <summary>Policies for the coarse roles, resource handlers for stores/employees, claims mapping, local cache.</summary>
    public static IServiceCollection AddSharedAuthorization(this IServiceCollection services, Action<SharedAuthorizationOptions>? configure = null)
    {
        var options = new SharedAuthorizationOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);

        services.TryAddSingleton<IPermissionCache, InMemoryPermissionCache>();
        services.AddSingleton<IClaimsTransformation, AppClaimsTransformation>();
        services.AddSingleton<IAuthorizationHandler, StoreAccessHandler>();
        services.AddSingleton<IAuthorizationHandler, EmployeeAccessHandler>();

        // Store-manager policy: StoreManager or Executive; Hr gets its own policies (decided 2026-10-21).
        services.AddAuthorizationBuilder()
            .AddPolicy(AppPolicies.RequireEmployee, p => p.RequireAuthenticatedUser().RequireClaim(AppClaimTypes.Role, AppRoles.All))
            .AddPolicy(AppPolicies.RequireStoreManager, p => p.RequireAuthenticatedUser().RequireClaim(AppClaimTypes.Role, AppRoles.StoreManager, AppRoles.Executive))
            .AddPolicy(AppPolicies.RequireHr, p => p.RequireAuthenticatedUser().RequireClaim(AppClaimTypes.Role, AppRoles.Hr))
            .AddPolicy(AppPolicies.RequireExecutive, p => p.RequireAuthenticatedUser().RequireClaim(AppClaimTypes.Role, AppRoles.Executive))
            .AddPolicy(AppPolicies.CanAccessStore, p => p.RequireAuthenticatedUser().AddRequirements(new StoreAccessRequirement()))
            .AddPolicy(AppPolicies.CanAccessEmployee, p => p.RequireAuthenticatedUser().AddRequirements(new EmployeeAccessRequirement()));

        var policies = services.AddAuthorizationBuilder();
        foreach (var role in options.ServiceRoles)
        {
            policies.AddPolicy(AppPolicies.ServiceRole(role), p => p.RequireAuthenticatedUser().RequireClaim(AppClaimTypes.ServiceRole, role));
        }

        return services;
    }
}
