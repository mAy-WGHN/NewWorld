using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Shared.Authorization;

public static class AuthorizationServiceCollectionExtensions
{
    /// <summary>Privilege policies against the local permission copy, service role policies and the claims mapping.</summary>
    public static IServiceCollection AddSharedAuthorization(this IServiceCollection services, Action<SharedAuthorizationOptions>? configure = null)
    {
        var options = new SharedAuthorizationOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);

        services.TryAddSingleton<IPermissionCache, InMemoryPermissionCache>();
        services.AddSingleton<IClaimsTransformation, AppClaimsTransformation>();
        services.AddSingleton<IAuthorizationHandler, PrivilegeHandler>();
        services.AddSingleton<IAuthorizationPolicyProvider, PrivilegePolicyProvider>();

        var policies = services.AddAuthorizationBuilder();
        foreach (var role in options.ServiceRoles)
        {
            policies.AddPolicy(AppPolicies.ServiceRole(role), p => p.RequireAuthenticatedUser().RequireClaim(AppClaimTypes.ServiceRole, role));
        }

        return services;
    }
}
