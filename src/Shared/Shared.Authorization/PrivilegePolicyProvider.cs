using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Shared.Authorization;

/// <summary>Builds privilege policies on demand, so APIs do not have to register one policy per privilege.</summary>
public sealed class PrivilegePolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(AppPolicies.PrivilegePrefix, StringComparison.Ordinal))
        {
            return new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PrivilegeRequirement(policyName[AppPolicies.PrivilegePrefix.Length..]))
                .Build();
        }

        return await base.GetPolicyAsync(policyName);
    }
}
