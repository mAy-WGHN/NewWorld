using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Portal.Core;
using Portal.Core.Reports;
using Portal.Infrastructure;
using Portal.Infrastructure.Reports;
using Shared.Testing;
using static Portal.Api.Tests.PortalApi;

namespace Portal.Api.Tests;

/// <summary>
/// Statistics of reports by creation time (UTC, archived reports count). Combinations that do not occur are 0, months and stores without reports are missing.
/// A report with several stores counts in each of them. A limited reach counts only the reports that touch its stores.
/// </summary>
public sealed class StatisticsEndpointTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static DateTime At(int year, int month, int day, int hour = 12) => new(year, month, day, hour, 0, 0, DateTimeKind.Utc);

    private static async Task SeedAsync(
        PortalApi api, DateTime createdAt, ReportCategory category = ReportCategory.Bug, ReportSeverity severity = ReportSeverity.Low,
        ReportSource source = ReportSource.Employee, int personnelNumber = 1, bool archived = false, params Guid[] stores)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var record = new ReportRecord
        {
            PersonnelNumber = personnelNumber, Category = category, Severity = severity, Source = source, Archived = archived,
            Description = "x", CreatedAt = createdAt, UpdatedAt = createdAt,
        };
        foreach (var store in stores.Length == 0 ? [StoreA] : stores)
        {
            record.Locations.Add(new ReportLocationRecord { LocationId = store });
        }

        db.Reports.Add(record);
        await db.SaveChangesAsync();
    }

    private static async Task<List<JsonElement>> GetAsync(PortalApi api, string url, Guid person = default)
    {
        using var response = await api.SendAsync(HttpMethod.Get, url, person == default ? Hanna : person);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return [.. (await DataAsync(response)).EnumerateArray()];
    }

    private static int[] Numbers(JsonElement row, params string[] fields) => [.. fields.Select(f => row.GetProperty(f).GetInt32())];

    // ------------------------------------------------------------------ monthly

    [Fact]
    public async Task Monthly_counts_the_sources_per_month_in_order_and_archived_reports_count()
    {
        await using var api = await StartAsync(postgres);
        await SeedAsync(api, At(2026, 3, 1, 0));
        await SeedAsync(api, At(2026, 3, 31, 23), source: ReportSource.System);
        await SeedAsync(api, At(2026, 3, 15), archived: true);
        await SeedAsync(api, At(2025, 12, 31, 23));
        await SeedAsync(api, At(2026, 5, 2), source: ReportSource.System); // no report in April: April is missing

        var rows = await GetAsync(api, "/api/statistics/monthly");

        rows.Select(r => r.GetProperty("month").GetString()).ShouldBe(["2025-12", "2026-03", "2026-05"]);
        Numbers(rows[1], "employee", "system", "total").ShouldBe([2, 1, 3]);
        Numbers(rows[0], "employee", "system", "total").ShouldBe([1, 0, 1]);
        Numbers(rows[2], "employee", "system", "total").ShouldBe([0, 1, 1]);
    }

    [Fact]
    public async Task The_range_is_inclusive_from_the_first_of_the_first_month_to_the_end_of_the_last_month()
    {
        await using var api = await StartAsync(postgres);
        await SeedAsync(api, At(2026, 1, 31, 23));   // before
        await SeedAsync(api, At(2026, 2, 1, 0));     // first moment of February
        await SeedAsync(api, At(2028, 2, 29, 23));   // leap day, last day of the range
        await SeedAsync(api, At(2028, 3, 1, 0));     // after

        (await GetAsync(api, "/api/statistics/monthly?from=2026-02&to=2028-02")).Select(r => r.GetProperty("month").GetString()).ShouldBe(["2026-02", "2028-02"]);
        (await GetAsync(api, "/api/statistics/monthly?from=2028-02&to=2028-02")).Select(r => r.GetProperty("month").GetString()).ShouldBe(["2028-02"]);
        (await GetAsync(api, "/api/statistics/monthly?to=2026-01")).Select(r => r.GetProperty("month").GetString()).ShouldBe(["2026-01"]);
        (await GetAsync(api, "/api/statistics/monthly?from=2029-01")).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("from=2026-13", "from")]
    [InlineData("to=2026-1", "to")]
    [InlineData("from=abc", "from")]
    public async Task A_range_in_the_wrong_format_is_a_422_naming_the_field(string query, string field)
    {
        await using var api = await StartAsync(postgres);

        using var response = await api.SendAsync(HttpMethod.Get, "/api/statistics/monthly?" + query, Hanna);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var error = (await ErrorAsync(response)).GetProperty("errors")[0];
        error.GetProperty("field").GetString().ShouldBe(field);
        error.GetProperty("message").GetString().ShouldBe("Format muss YYYY-MM sein.");
    }

    // ------------------------------------------------------------------ by category, store, person

    [Fact]
    public async Task By_category_counts_every_category_per_month()
    {
        await using var api = await StartAsync(postgres);
        await SeedAsync(api, At(2026, 3, 5), ReportCategory.Bug);
        await SeedAsync(api, At(2026, 3, 6), ReportCategory.Bug);
        await SeedAsync(api, At(2026, 3, 7), ReportCategory.Theft);
        await SeedAsync(api, At(2026, 3, 8), ReportCategory.Suggestion);
        await SeedAsync(api, At(2026, 4, 8), ReportCategory.Virus);
        await SeedAsync(api, At(2026, 4, 9), ReportCategory.Other);

        var rows = await GetAsync(api, "/api/statistics/monthly-by-category");

        rows.Select(r => r.GetProperty("month").GetString()).ShouldBe(["2026-03", "2026-04"]);
        Numbers(rows[0], "bug", "virus", "theft", "other", "suggestion", "total").ShouldBe([2, 0, 1, 0, 1, 4]);
        Numbers(rows[1], "bug", "virus", "theft", "other", "suggestion", "total").ShouldBe([0, 1, 0, 1, 0, 2]);
    }

    [Fact]
    public async Task By_store_a_report_with_several_stores_counts_in_each_and_the_stores_are_sorted_by_name()
    {
        await using var api = await StartAsync(postgres);
        await SeedAsync(api, At(2026, 3, 5), severity: ReportSeverity.Critical, stores: [StoreB, StoreA]);
        await SeedAsync(api, At(2026, 3, 6), severity: ReportSeverity.Low, stores: [StoreB]);
        await SeedAsync(api, At(2026, 3, 7), severity: ReportSeverity.High, stores: [StoreC]);

        var rows = await GetAsync(api, "/api/statistics/by-location-severity");

        rows.Select(r => r.GetProperty("locationName").GetString()).ShouldBe(["Store A", "Store B", "Store C"]);
        rows.Select(r => r.GetProperty("locationId").GetGuid()).ShouldBe([StoreA, StoreB, StoreC]);
        Numbers(rows[0], "low", "medium", "high", "critical", "total").ShouldBe([0, 0, 0, 1, 1]);
        Numbers(rows[1], "low", "medium", "high", "critical", "total").ShouldBe([1, 0, 0, 1, 2]);
        Numbers(rows[2], "low", "medium", "high", "critical", "total").ShouldBe([0, 0, 1, 0, 1]);
    }

    [Fact]
    public async Task By_personnel_number_counts_the_severities_in_order_of_the_number()
    {
        await using var api = await StartAsync(postgres);
        await SeedAsync(api, At(2026, 3, 5), severity: ReportSeverity.High, personnelNumber: 900);
        await SeedAsync(api, At(2026, 3, 6), severity: ReportSeverity.Low, personnelNumber: 12);
        await SeedAsync(api, At(2026, 3, 7), severity: ReportSeverity.Low, personnelNumber: 12);
        await SeedAsync(api, At(2026, 3, 8), severity: ReportSeverity.Medium, personnelNumber: 12);

        var rows = await GetAsync(api, "/api/statistics/by-personnel-number");

        rows.Select(r => r.GetProperty("personnelNumber").GetInt32()).ShouldBe([12, 900]);
        Numbers(rows[0], "low", "medium", "high", "critical", "total").ShouldBe([2, 1, 0, 0, 3]);
        Numbers(rows[1], "low", "medium", "high", "critical", "total").ShouldBe([0, 0, 1, 0, 1]);
    }

    [Theory]
    [InlineData("monthly")]
    [InlineData("monthly-by-category")]
    [InlineData("by-location-severity")]
    [InlineData("by-personnel-number")]
    public async Task Nothing_in_the_range_is_an_empty_list(string endpoint)
    {
        await using var api = await StartAsync(postgres);

        (await GetAsync(api, $"/api/statistics/{endpoint}")).ShouldBeEmpty();
    }

    // ------------------------------------------------------------------ privilege and reach

    [Theory]
    [InlineData("monthly")]
    [InlineData("monthly-by-category")]
    [InlineData("by-location-severity")]
    [InlineData("by-personnel-number")]
    public async Task Every_statistic_needs_the_privilege(string endpoint)
    {
        await using var api = await StartAsync(postgres);
        api.Grant(Erika, TestGrant.Everywhere(PortalPrivileges.ReportRead)); // reading reports is not enough

        using var denied = await api.SendAsync(HttpMethod.Get, $"/api/statistics/{endpoint}", Erika);
        using var anonymous = await api.SendAsync(HttpMethod.Get, $"/api/statistics/{endpoint}", person: null);

        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_limited_reach_counts_only_the_reports_that_touch_its_stores()
    {
        await using var api = await StartAsync(postgres);
        await SeedAsync(api, At(2026, 3, 5), personnelNumber: 1, stores: [StoreA]);
        await SeedAsync(api, At(2026, 3, 6), personnelNumber: 2, stores: [StoreA, StoreB]);
        await SeedAsync(api, At(2026, 3, 7), personnelNumber: 3, stores: [StoreC]);
        api.Grant(Ben, new TestGrant(PortalPrivileges.StatisticRead, [StoreB]));

        var monthly = await GetAsync(api, "/api/statistics/monthly", Ben);
        var persons = await GetAsync(api, "/api/statistics/by-personnel-number", Ben);

        Numbers(monthly.Single(), "total").ShouldBe([1]);
        persons.Select(r => r.GetProperty("personnelNumber").GetInt32()).ShouldBe([2]);
    }
}
