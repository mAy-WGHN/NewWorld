namespace Shared.Authorization;

public sealed class SharedAuthorizationOptions
{
    /// <summary>
    /// Service roles of this API, one per capability, e.g. <c>Workforce.AbsenceDetails.Read</c>. Each gets a policy
    /// <see cref="AppPolicies.ServiceRole"/>. Which calling service holds which role is configured in <c>ServiceAuth:Peers</c>.
    /// </summary>
    public ISet<string> ServiceRoles { get; } = new HashSet<string>(StringComparer.Ordinal);
}
