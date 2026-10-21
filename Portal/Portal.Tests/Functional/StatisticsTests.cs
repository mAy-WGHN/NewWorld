using System.Net;
using System.Text.Json;
using Portal.Domain.Enums;
using Portal.Tests.Infrastructure;

namespace Portal.Tests.Functional;

/// <summary>
/// Die Statistik aggregiert über alle Reports. Jeder Test nutzt deshalb eigene, weit zurückliegende Monate
/// (2001–2003) und filtert per from/to darauf, damit Daten anderer Tests nicht stören.
/// </summary>
public class StatisticsTests(ApiFactory factory) : ApiTestBase(factory)
{
    private static JsonElement Row(JsonElement data, string key, string value) =>
        data.EnumerateArray().Single(r => r.GetProperty(key).ToString() == value);

    [Theory]
    [InlineData("monthly")]
    [InlineData("monthly-by-category")]
    [InlineData("by-location-severity")]
    [InlineData("by-personnel-number")]
    public async Task Endpoints_RequireAdmin(string path)
    {
        var response = await GetAsync($"/api/statistics/{path}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("monthly", "from=2001-13")]
    [InlineData("monthly", "to=2001-1")]
    [InlineData("monthly-by-category", "from=kaputt")]
    [InlineData("by-location-severity", "to=2001/05")]
    [InlineData("by-personnel-number", "from=20010501")]
    public async Task BadDateFormat_Returns422(string path, string query)
    {
        var (_, token) = await AdminWithTokenAsync();

        var response = await GetAsync($"/api/statistics/{path}?{query}", token);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("Format muss YYYY-MM sein.",
            (await JsonAsync(response)).GetProperty("errors")[0].GetProperty("message").GetString());
    }

    [Fact]
    public async Task Monthly_BreaksDownBySource_AscendingByMonth_AndSkipsEmptyMonths()
    {
        var (_, token) = await AdminWithTokenAsync();
        var location = await CreateLocationAsync();
        await CreateReportAsync(location, createdAt: new DateTime(2001, 1, 5));
        await CreateReportAsync(location, createdAt: new DateTime(2001, 1, 20));
        await CreateReportAsync(location, createdAt: new DateTime(2001, 1, 21), source: ReportSource.System);
        await CreateReportAsync(location, createdAt: new DateTime(2001, 3, 2));

        var data = await DataAsync(await GetAsync("/api/statistics/monthly?from=2001-01&to=2001-03", token));

        Assert.Equal(["2001-01", "2001-03"], data.EnumerateArray().Select(r => r.GetProperty("month").GetString()!).ToArray());
        var january = Row(data, "month", "2001-01");
        Assert.Equal(2, january.GetProperty("employee").GetInt32());
        Assert.Equal(1, january.GetProperty("system").GetInt32());
        Assert.Equal(3, january.GetProperty("total").GetInt32());
        var march = Row(data, "month", "2001-03");
        Assert.Equal(0, march.GetProperty("system").GetInt32());
        Assert.Equal(1, march.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task MonthlyByCategory_CountsEveryCategory_WithZerosForMissingOnes()
    {
        var (_, token) = await AdminWithTokenAsync();
        var location = await CreateLocationAsync();
        var month = new DateTime(2001, 8, 10);
        await CreateReportAsync(location, ReportCategory.Bug, createdAt: month);
        await CreateReportAsync(location, ReportCategory.Bug, createdAt: month);
        await CreateReportAsync(location, ReportCategory.Virus, createdAt: month);
        await CreateReportAsync(location, ReportCategory.Suggestion, createdAt: month);

        var data = await DataAsync(await GetAsync("/api/statistics/monthly-by-category?from=2001-08&to=2001-08", token));

        var row = Row(data, "month", "2001-08");
        Assert.Equal(2, row.GetProperty("bug").GetInt32());
        Assert.Equal(1, row.GetProperty("virus").GetInt32());
        Assert.Equal(0, row.GetProperty("theft").GetInt32());
        Assert.Equal(0, row.GetProperty("other").GetInt32());
        Assert.Equal(1, row.GetProperty("suggestion").GetInt32());
        Assert.Equal(4, row.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task FromAndTo_ExcludeReportsOutsideTheRange()
    {
        var (_, token) = await AdminWithTokenAsync();
        var location = await CreateLocationAsync();
        await CreateReportAsync(location, createdAt: new DateTime(2002, 5, 15));
        await CreateReportAsync(location, createdAt: new DateTime(2002, 6, 15));
        await CreateReportAsync(location, createdAt: new DateTime(2002, 7, 15));

        var data = await DataAsync(await GetAsync("/api/statistics/monthly?from=2002-06&to=2002-06", token));

        Assert.Equal(["2002-06"], data.EnumerateArray().Select(r => r.GetProperty("month").GetString()!).ToArray());
    }

    [Fact]
    public async Task To_IncludesTheWholeMonth()
    {
        var (_, token) = await AdminWithTokenAsync();
        var location = await CreateLocationAsync();
        await CreateReportAsync(location, createdAt: new DateTime(2002, 9, 30, 23, 59, 59));

        var data = await DataAsync(await GetAsync("/api/statistics/monthly?from=2002-09&to=2002-09", token));

        Assert.Equal(1, Row(data, "month", "2002-09").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task ArchivedReports_AreCountedToo()
    {
        var (_, token) = await AdminWithTokenAsync();
        var location = await CreateLocationAsync();
        await CreateReportAsync(location, archived: true, createdAt: new DateTime(2002, 11, 3));

        var data = await DataAsync(await GetAsync("/api/statistics/monthly?from=2002-11&to=2002-11", token));

        Assert.Equal(1, Row(data, "month", "2002-11").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task ByLocationSeverity_CountsAMultiLocationReportInEveryLocation_SortedByName()
    {
        var (_, token) = await AdminWithTokenAsync();
        var berlin = await CreateLocationAsync(name: "Berlin " + Unique("L"));
        var aachen = await CreateLocationAsync(name: "Aachen " + Unique("L"));
        var date = new DateTime(2003, 2, 10);
        await CreateReportAsync(berlin, severity: ReportSeverity.High, createdAt: date, source: ReportSource.Employee, extraLocations: aachen);
        await CreateReportAsync(berlin, severity: ReportSeverity.Low, createdAt: date);

        var data = await DataAsync(await GetAsync("/api/statistics/by-location-severity?from=2003-02&to=2003-02", token));

        var names = data.EnumerateArray().Select(r => r.GetProperty("locationName").GetString()!).ToList();
        Assert.Equal([aachen.Name, berlin.Name], names); // nach Filialname sortiert
        var aachenRow = Row(data, "locationId", aachen.Id.ToString());
        Assert.Equal(1, aachenRow.GetProperty("high").GetInt32());
        Assert.Equal(1, aachenRow.GetProperty("total").GetInt32());
        var berlinRow = Row(data, "locationId", berlin.Id.ToString());
        Assert.Equal(1, berlinRow.GetProperty("high").GetInt32());
        Assert.Equal(1, berlinRow.GetProperty("low").GetInt32());
        Assert.Equal(0, berlinRow.GetProperty("critical").GetInt32());
        Assert.Equal(2, berlinRow.GetProperty("total").GetInt32());
        // Die Summe über alle Filialen ist größer als die Report-Anzahl (2 Reports, 3 Zählungen).
        Assert.Equal(3, data.EnumerateArray().Sum(r => r.GetProperty("total").GetInt32()));
    }

    [Fact]
    public async Task ByPersonnelNumber_BreaksDownBySeverity_AscendingByNumber()
    {
        var (_, token) = await AdminWithTokenAsync();
        var location = await CreateLocationAsync();
        var date = new DateTime(2003, 4, 1);
        await CreateReportAsync(location, severity: ReportSeverity.Critical, personnelNumber: 2002, createdAt: date);
        await CreateReportAsync(location, severity: ReportSeverity.Medium, personnelNumber: 1001, createdAt: date);
        await CreateReportAsync(location, severity: ReportSeverity.High, personnelNumber: 1001, createdAt: date);

        var data = await DataAsync(await GetAsync("/api/statistics/by-personnel-number?from=2003-04&to=2003-04", token));

        Assert.Equal([1001, 2002], data.EnumerateArray().Select(r => r.GetProperty("personnelNumber").GetInt32()).ToArray());
        var first = Row(data, "personnelNumber", "1001");
        Assert.Equal(1, first.GetProperty("medium").GetInt32());
        Assert.Equal(1, first.GetProperty("high").GetInt32());
        Assert.Equal(0, first.GetProperty("critical").GetInt32());
        Assert.Equal(2, first.GetProperty("total").GetInt32());
        Assert.Equal(1, Row(data, "personnelNumber", "2002").GetProperty("critical").GetInt32());
    }
}
