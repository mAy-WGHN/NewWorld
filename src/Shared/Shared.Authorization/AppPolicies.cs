namespace Shared.Authorization;

/// <summary>Policy names. Privileges come from the ACL of the Access service (decision 13); there are no coarse roles.</summary>
public static class AppPolicies
{
    internal const string PrivilegePrefix = "App.Privilege.";
    internal const string ServiceRolePrefix = "App.Service.";
    internal const string PrivilegeWithSecondFactorPrefix = "App.PrivilegeMfa.";

    /// <summary>
    /// Policy for an ACL privilege <c>resource:action</c>. Without resource: the person has the privilege anywhere.
    /// With <see cref="StoreResource"/> or <see cref="EmployeeResource"/>: the privilege reaches that store or person.
    /// </summary>
    public static string Privilege(string key) => PrivilegePrefix + key;

    /// <summary>
    /// Like <see cref="Privilege"/>, and in addition the person must have confirmed a second factor recently (<see cref="SecondFactorRequirement"/>).
    /// If only the second factor is missing, the answer is 403 with the problem title <c>SecondFactorRequired</c> so that a frontend can start the
    /// step-up; if the privilege is missing it stays the plain 403.
    /// </summary>
    public static string PrivilegeWithSecondFactor(string key) => PrivilegeWithSecondFactorPrefix + key;

    /// <summary>Policy for a service role registered in <see cref="SharedAuthorizationOptions.ServiceRoles"/>.</summary>
    public static string ServiceRole(string role) => ServiceRolePrefix + role;
}
