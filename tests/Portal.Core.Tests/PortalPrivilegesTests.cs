namespace Portal.Core.Tests;

public class PortalPrivilegesTests
{
    [Fact]
    public void Every_privilege_is_described_and_unique_and_there_is_none_for_creating_reports()
    {
        PortalPrivileges.Catalog.Where(c => string.IsNullOrWhiteSpace(c.Description)).ShouldBeEmpty();
        PortalPrivileges.Catalog.Select(c => c.Key).Distinct().Count().ShouldBe(PortalPrivileges.Catalog.Count);
        PortalPrivileges.All.ShouldNotContain("report:create"); // creating a report is public
    }

    [Fact]
    public void Reports_and_statistics_can_be_scoped_to_stores_the_FAQ_cannot() =>
        PortalPrivileges.Catalog.Where(c => c.Scopable).Select(c => c.Key)
            .ShouldBe([PortalPrivileges.ReportRead, PortalPrivileges.ReportUpdate, PortalPrivileges.ReportDelete, PortalPrivileges.StatisticRead], ignoreOrder: true);
}
