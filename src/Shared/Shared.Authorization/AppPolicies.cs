namespace Shared.Authorization;

/// <summary>Policy names. Privileges come from the ACL of the Access service (decision 13); there are no coarse roles.</summary>
public static class AppPolicies
{
    internal const string PrivilegePrefix = "App.Privilege.";
    internal const string ServiceRolePrefix = "App.Service.";

    /// <summary>
    /// Policy for an ACL privilege <c>resource:action</c>. Without resource: the person has the privilege anywhere.
    /// With <see cref="StoreResource"/> or <see cref="EmployeeResource"/>: the privilege reaches that store or person.
    /// </summary>
    public static string Privilege(string key) => PrivilegePrefix + key;

    /// <summary>Policy for a service app role registered in <see cref="SharedAuthorizationOptions.ServiceRoles"/>.</summary>
    public static string ServiceRole(string role) => ServiceRolePrefix + role;
}
