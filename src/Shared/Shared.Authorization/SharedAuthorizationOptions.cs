namespace Shared.Authorization;

public sealed class SharedAuthorizationOptions
{
    /// <summary>
    /// App roles of other services this API accepts (client credentials), one per capability, e.g.
    /// <c>Workforce.AbsenceDetails.Read</c>. Each gets a policy <see cref="AppPolicies.ServiceRole"/>.
    /// </summary>
    public ISet<string> ServiceRoles { get; } = new HashSet<string>(StringComparer.Ordinal);
}
