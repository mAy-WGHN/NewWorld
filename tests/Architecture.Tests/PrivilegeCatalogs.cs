using Access.Core.Acl;
using Notifications.Core;
using Portal.Core;
using Workforce.Core.Absences;
using Workforce.Core.Certificates;
using Workforce.Core.Employees;
using Workforce.Core.Unavailability;
using Workforce.Core.WorkingTime;

namespace Architecture.Tests;

/// <summary>The privileges every service registers with Access (the same lists the services send as catalogue).</summary>
internal static class PrivilegeCatalogs
{
    public static IReadOnlyList<(string Key, string Description)> All { get; } =
    [
        .. AccessPrivileges.Catalog.Select(c => (c.Key, c.Description)),
        .. AbsencePrivileges.Catalog.Concat(CertificatePrivileges.Catalog).Concat(EmployeePrivileges.Catalog)
            .Concat(UnavailabilityPrivileges.Catalog).Concat(WorkingTimePrivileges.Catalog).Select(c => (c.Key, c.Description)),
        .. NotificationPrivileges.Catalog.Select(c => (c.Key, c.Description)),
        .. PortalPrivileges.Catalog.Select(c => (c.Key, c.Description)),
    ];
}
