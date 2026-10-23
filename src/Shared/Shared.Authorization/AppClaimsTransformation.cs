using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace Shared.Authorization;

/// <summary>
/// The only place that reads token claims. Maps <c>employee_id</c> (person, in tokens issued by Access) to
/// <see cref="AppClaimTypes.EmployeeId"/> and configured service roles from <c>roles</c> to <see cref="AppClaimTypes.ServiceRole"/>.
/// Everything else (e.g. Entra user roles or object IDs) is ignored: permissions come only from the ACL (decision 13).
/// </summary>
public sealed class AppClaimsTransformation(SharedAuthorizationOptions options) : IClaimsTransformation
{
    private const string EmployeeIdClaim = "employee_id";
    private const string RolesClaim = "roles";
    private const string MarkerAuthenticationType = "Shared.Authorization";

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        // Called on every authenticate; run only once per principal.
        if (principal.Identities.Any(i => i.AuthenticationType == MarkerAuthenticationType))
        {
            return Task.FromResult(principal);
        }

        var mapped = new ClaimsIdentity(MarkerAuthenticationType);
        if (principal.FindFirst(EmployeeIdClaim)?.Value is { } value && Guid.TryParse(value, out var employeeId))
        {
            mapped.AddClaim(new Claim(AppClaimTypes.EmployeeId, employeeId.ToString()));
        }

        foreach (var role in principal.FindAll(RolesClaim).Select(c => c.Value).Where(options.ServiceRoles.Contains).Distinct())
        {
            mapped.AddClaim(new Claim(AppClaimTypes.ServiceRole, role));
        }

        principal.AddIdentity(mapped);
        return Task.FromResult(principal);
    }
}
