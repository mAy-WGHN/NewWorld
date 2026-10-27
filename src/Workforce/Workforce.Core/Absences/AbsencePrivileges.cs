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

    /// <summary>
    /// Full view: period, status and type (sickness/vacation): the person themself, supervisors and HR. There is deliberately no
    /// level in between: the status of a sick note ("reported") would reveal the type.
    /// </summary>
    public const string ReadReason = "absence:read-reason";

    /// <summary>Sees the BC transfer status.</summary>
    public const string ReadSync = "absence:read-sync";

    public const string Create = "absence:create";
    public const string Approve = "absence:approve";
    public const string ManageEntitlement = "absence:manage-entitlement";

    /// <summary>
    /// Sets whether a store is open on public holidays (e.g. railway-station stores); scopable, so district managers maintain
    /// the stores of their district.
    /// </summary>
    public const string ManageHolidayOpening = "absence:manage-holiday-opening";

    /// <summary>Sets when additional vacation days (leftover, special leave) expire, for everybody (HR); not scopable.</summary>
    public const string ManageVacationRules = "absence:manage-vacation-rules";

    public static readonly IReadOnlyList<(string Key, bool Scopable)> Catalog =
    [
        (ReadOwn, false), (CreateOwn, false), (UpdateOwn, false),
        (ReadAvailability, true), (ReadReason, true), (ReadSync, true), (Create, true), (Approve, true), (ManageEntitlement, true), (ManageHolidayOpening, true), (ManageVacationRules, false),
    ];
}
