namespace Workforce.Core.Absences;

/// <summary>
/// Privileges of the absence endpoints, registered with Access (the ACL decides who gets them, with which reach). Reach for
/// scopable ones comes from Access: the stores and employees of the assignment.
/// </summary>
public static class AbsencePrivileges
{
    public const string AppKey = "absence";

    // own data
    public const string ReadOwn = "absence:read-own";
    public const string CreateOwn = "absence:create-own";
    public const string UpdateOwn = "absence:update-own";

    // others (scopable: stores / employees in reach)
    /// <summary>Sees that colleagues are absent ("abwesend"), nothing more.</summary>
    public const string ReadAvailability = "absence:read-availability";

    /// <summary>Sees absences with period and status, but not the type.</summary>
    public const string Read = "absence:read";

    /// <summary>Sees the type (sickness/vacation) too: the person, supervisors and HR.</summary>
    public const string ReadReason = "absence:read-reason";

    /// <summary>Sees the BC transfer status.</summary>
    public const string ReadSync = "absence:read-sync";

    public const string Create = "absence:create";
    public const string Approve = "absence:approve";
    public const string ManageEntitlement = "absence:manage-entitlement";

    public static readonly IReadOnlyList<(string Key, bool Scopable)> Catalog =
    [
        (ReadOwn, false), (CreateOwn, false), (UpdateOwn, false),
        (ReadAvailability, true), (Read, true), (ReadReason, true), (ReadSync, true), (Create, true), (Approve, true), (ManageEntitlement, true),
    ];
}
