namespace Shared.Authorization;

/// <summary>
/// Internal claim types. Feature code sees only these; provider claims (<c>roles</c>, <c>oid</c>) are read
/// exclusively inside <c>Shared.Authorization</c> (enforced by an architecture test).
/// </summary>
public static class AppClaimTypes
{
    public const string Role = "app:role";
    public const string EmployeeId = "app:employee_id";

    /// <summary>App role of a calling service (service-to-service), see <see cref="SharedAuthorizationOptions.ServiceRoles"/>.</summary>
    public const string ServiceRole = "app:service_role";
}
