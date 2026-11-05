namespace Shared.Authorization;

/// <summary>
/// Internal claim types. Feature code sees only these; token claims (<c>employee_id</c>) are read
/// exclusively inside <c>Shared.Authorization</c> (enforced by an architecture test).
/// </summary>
public static class AppClaimTypes
{
    /// <summary>The signed-in person (from the token issued by Access).</summary>
    public const string EmployeeId = "app:employee_id";

    /// <summary>Capability (service role) of a calling service, granted per peer in <c>ServiceAuth:Peers</c>, see <see cref="SharedAuthorizationOptions.ServiceRoles"/>.</summary>
    public const string ServiceRole = "app:service_role";

    /// <summary>Name of the calling service (<see cref="ServiceNames"/>), set after its signed token has been checked.</summary>
    public const string ServiceName = "app:service_name";

    /// <summary>
    /// Until when the person has confirmed a second factor (Unix seconds), mapped from the token claim <see cref="SecondFactorUntilTokenClaim"/>.
    /// Only tokens issued by the step-up flow of Access carry it; see <see cref="SecondFactorRequirement"/>.
    /// </summary>
    public const string SecondFactorUntil = "app:mfa_until";

    /// <summary>Name of the claim in person tokens (written by Access, read only by <see cref="AppClaimsTransformation"/>).</summary>
    public const string SecondFactorUntilTokenClaim = "mfa_until";
}
