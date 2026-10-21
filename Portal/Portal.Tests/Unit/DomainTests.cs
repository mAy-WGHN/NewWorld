using Portal.Domain.Entities;
using Portal.Domain.Enums;

namespace Portal.Tests.Unit;

public class ReportSeverityTests
{
    [Fact]
    public void Rank_IsAscending_AndEveryRankIsUnique()
    {
        var ranks = new[] { ReportSeverity.Low, ReportSeverity.Medium, ReportSeverity.High, ReportSeverity.Critical }
            .Select(s => s.Rank()).ToArray();

        Assert.Equal([1, 2, 3, 4], ranks);
        Assert.Equal(ranks.Length, ranks.Distinct().Count());
    }

    [Fact]
    public void Rank_DiffersFromTheAlphabeticalOrder()
    {
        var byName = Enum.GetValues<ReportSeverity>().OrderBy(s => s.ToString(), StringComparer.Ordinal).ToList();
        var byRank = Enum.GetValues<ReportSeverity>().OrderBy(s => s.Rank()).ToList();

        Assert.NotEqual(byName, byRank);
    }
}

public class ReportEntityTests
{
    private static Guid NewLocation() => Guid.CreateVersion7();

    [Fact]
    public void NewReport_HasTheDocumentedDefaults()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);

        var report = new Report(123, ReportSource.Employee, ReportCategory.Bug, ReportSeverity.High, "Text", [NewLocation()]);

        Assert.Equal(ReportStatus.Open, report.Status);
        Assert.False(report.Archived);
        Assert.Null(report.Comment);
        Assert.Null(report.ParentReportId);
        Assert.Empty(report.ChildReports);
        Assert.Equal(7, report.Id.Version); // UUIDv7
        Assert.True(report.CreatedAt >= before);
        Assert.Equal(report.CreatedAt, report.UpdatedAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void AddLocation_IsIdempotent()
    {
        var location = NewLocation();
        var report = new Report(1, ReportSource.Employee, ReportCategory.Bug, ReportSeverity.Low, "x", [location]);

        report.AddLocation(location);
        report.AddLocation(location);

        Assert.Single(report.ReportLocations);
    }

    [Fact]
    public void Constructor_IgnoresDuplicateLocations()
    {
        var location = NewLocation();

        var report = new Report(1, ReportSource.Employee, ReportCategory.Bug, ReportSeverity.Low, "x", [location, location]);

        Assert.Single(report.ReportLocations);
    }

    [Fact]
    public void Source_CannotBeChangedAfterCreation()
    {
        var setter = typeof(Report).GetProperty(nameof(Report.Source))!.SetMethod!;

        Assert.False(setter.IsPublic);
    }
}

public class FaqImageEntityTests
{
    [Fact]
    public void FaqImage_UsesUuidV4_ButEverythingElseUuidV7()
    {
        Assert.Equal(4, new FaqImage().Id.Version);
        Assert.Equal(7, new Faq().Id.Version);
    }
}
