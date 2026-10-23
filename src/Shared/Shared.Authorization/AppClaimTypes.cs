namespace Shared.Authorization;

/// <summary>
/// Internal claim types. Feature code sees only these; token claims (<c>employee_id</c>, <c>roles</c>) are read
/// exclusively inside <c>Shared.Authorization</c> (enforced by an architecture test).
/// </summary>
public static class AppClaimTypes
{
    /// <summary>The signed-in person (from the token issued by Access).</summary>
    public const string EmployeeId = "app:employee_id";

    /// <summary>App role of a calling service (service-to-service), see <see cref="SharedAuthorizationOptions.ServiceRoles"/>.</summary>
    public const string ServiceRole = "app:service_role";
}
