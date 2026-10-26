using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace Shared.Authorization;

/// <summary>
/// The only place that reads claims of person tokens. Maps <c>employee_id</c> (in tokens issued by Access) to
/// <see cref="AppClaimTypes.EmployeeId"/>. Service roles are never taken from a token claim: they are set by the service
/// authentication from configuration (<see cref="ServiceKeyAuthenticationHandler"/>), so a person token cannot carry one.
/// Permissions come only from the ACL (decision 13).
/// </summary>
public sealed class AppClaimsTransformation : IClaimsTransformation
{
    private const string EmployeeIdClaim = "employee_id";
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

        principal.AddIdentity(mapped);
        return Task.FromResult(principal);
    }
}
