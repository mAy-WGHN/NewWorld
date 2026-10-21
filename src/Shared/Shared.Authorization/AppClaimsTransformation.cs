using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace Shared.Authorization;

/// <summary>
/// The only place that reads provider claims. Maps <c>roles</c> to <see cref="AppClaimTypes.Role"/> (known roles only)
/// and the object ID <c>oid</c> to <see cref="AppClaimTypes.EmployeeId"/> via the local cache. Replacing the identity
/// provider changes this class and the configuration, nothing else.
/// </summary>
public sealed class AppClaimsTransformation(IPermissionCache cache) : IClaimsTransformation
{
    private const string ProviderRoleClaim = "roles";
    private const string ProviderObjectIdClaim = "oid";
    private const string MarkerAuthenticationType = "Shared.Authorization";

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        // Called on every authenticate; run only once per principal.
        if (principal.Identities.Any(i => i.AuthenticationType == MarkerAuthenticationType))
        {
            return Task.FromResult(principal);
        }

        var mapped = new ClaimsIdentity(MarkerAuthenticationType);
        foreach (var role in principal.FindAll(ProviderRoleClaim).Select(c => c.Value).Where(AppRoles.All.Contains).Distinct())
        {
            mapped.AddClaim(new Claim(AppClaimTypes.Role, role));
        }

        var objectId = principal.FindFirst(ProviderObjectIdClaim)?.Value;
        if (objectId is not null && cache.TryResolveEmployeeId(objectId, out var employeeId))
        {
            mapped.AddClaim(new Claim(AppClaimTypes.EmployeeId, employeeId.ToString()));
        }

        principal.AddIdentity(mapped);
        return Task.FromResult(principal);
    }
}
