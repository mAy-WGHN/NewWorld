using Access.Core.Acl;
using Notifications.Core;
using Workforce.Core.Absences;
using Workforce.Core.Certificates;

namespace Architecture.Tests;

/// <summary>Every privilege explains in plain words what it allows, so that roles can be put together without guessing.</summary>
public class PrivilegeRulesTests
{
    [Fact]
    public void Every_privilege_of_every_service_has_a_description()
    {
        var missing = AccessPrivileges.Catalog.Select(c => (c.Key, c.Description))
            .Concat(AbsencePrivileges.Catalog.Concat(CertificatePrivileges.Catalog).Select(c => (c.Key, c.Description)))
            .Concat(NotificationPrivileges.Catalog.Select(c => (c.Key, c.Description)))
            .Where(c => string.IsNullOrWhiteSpace(c.Description))
            .Select(c => c.Key)
            .ToList();

        missing.ShouldBeEmpty("add a description (German, plain words) to the catalogue entry");
    }

    [Fact]
    public void Descriptions_fit_the_column_and_keys_are_unique()
    {
        var all = AccessPrivileges.Catalog.Select(c => (c.Key, c.Description))
            .Concat(AbsencePrivileges.Catalog.Concat(CertificatePrivileges.Catalog).Select(c => (c.Key, c.Description)))
            .Concat(NotificationPrivileges.Catalog.Select(c => (c.Key, c.Description)))
            .ToList();

        all.Where(c => c.Description.Length > 500).ShouldBeEmpty();
        all.GroupBy(c => c.Key).Where(g => g.Count() > 1).Select(g => g.Key).ShouldBeEmpty();
    }
}
