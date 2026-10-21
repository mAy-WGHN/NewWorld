using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Shared.Authorization;

public static class AuthorizationServiceCollectionExtensions
{
    /// <summary>Policies for the coarse roles, resource handlers for stores/employees, claims mapping, local cache.</summary>
    public static IServiceCollection AddSharedAuthorization(this IServiceCollection services)
    {
        services.TryAddSingleton<IPermissionCache, InMemoryPermissionCache>();
        services.AddSingleton<IClaimsTransformation, AppClaimsTransformation>();
        services.AddSingleton<IAuthorizationHandler, StoreAccessHandler>();
        services.AddSingleton<IAuthorizationHandler, EmployeeAccessHandler>();

        // TODO(Entscheidung): Soll Hr bzw. Executive die Filialleiter-Policy erfüllen? Bisher: StoreManager oder Executive.
        services.AddAuthorizationBuilder()
            .AddPolicy(AppPolicies.RequireEmployee, p => p.RequireAuthenticatedUser().RequireClaim(AppClaimTypes.Role, AppRoles.All))
            .AddPolicy(AppPolicies.RequireStoreManager, p => p.RequireAuthenticatedUser().RequireClaim(AppClaimTypes.Role, AppRoles.StoreManager, AppRoles.Executive))
            .AddPolicy(AppPolicies.RequireHr, p => p.RequireAuthenticatedUser().RequireClaim(AppClaimTypes.Role, AppRoles.Hr))
            .AddPolicy(AppPolicies.RequireExecutive, p => p.RequireAuthenticatedUser().RequireClaim(AppClaimTypes.Role, AppRoles.Executive))
            .AddPolicy(AppPolicies.CanAccessStore, p => p.RequireAuthenticatedUser().AddRequirements(new StoreAccessRequirement()))
            .AddPolicy(AppPolicies.CanAccessEmployee, p => p.RequireAuthenticatedUser().AddRequirements(new EmployeeAccessRequirement()));
        return services;
    }
}
