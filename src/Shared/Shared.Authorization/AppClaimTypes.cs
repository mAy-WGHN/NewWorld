namespace Shared.Authorization;

/// <summary>
/// Internal claim types. Feature code sees only these; provider claims (<c>roles</c>, <c>oid</c>) are read
/// exclusively inside <c>Shared.Authorization</c> (enforced by an architecture test).
/// </summary>
public static class AppClaimTypes
{
    public const string Role = "app:role";
    public const string EmployeeId = "app:employee_id";
}
