using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Portal.Infrastructure;
using Portal.Infrastructure.Logbook;
using Shared.Testing;
using static Portal.Api.Tests.PortalApi;

namespace Portal.Api.Tests;

/// <summary>
/// The local demo hierarchy of the Berichtsheft: one district and one region with the given stores and the two leaders, set up once the people and
/// stores have arrived as copies, never over a hierarchy somebody maintains. Store numbers in the test data: A = 017, B = 018.
/// </summary>
public sealed class LogbookDemoSeedTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string RegionalNumber = "10051";
    private const string DistrictNumber = "10052";

    private static LogbookDemoSeedService Service(PortalApi api, params string[] storeNumbers) =>
        new(
            api.Services.GetRequiredService<IServiceScopeFactory>(),
            new LogbookDemoSeedOptions { StoreNumbers = [.. storeNumbers], RegionalLeader = RegionalNumber, DistrictLeader = DistrictNumber },
            TimeProvider.System,
            NullLogger<LogbookDemoSeedService>.Instance);

    /// <summary>The copies of the two demo leaders get their personnel numbers (the test data has none).</summary>
    private static async Task GiveNumbersAsync(PortalApi api)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        (await db.People.SingleAsync(p => p.EmployeeId == Erika)).PersonnelNumber = RegionalNumber;
        (await db.People.SingleAsync(p => p.EmployeeId == Ben)).PersonnelNumber = DistrictNumber;
        await db.SaveChangesAsync();
    }

    private static async Task<System.Text.Json.JsonElement> StructureAsync(PortalApi api)
    {
        using var response = await api.SendAsync(HttpMethod.Get, "/api/logbook/structure", Hanna);
        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        return await DataAsync(response);
    }

    [Fact]
    public async Task Sets_up_a_district_and_a_region_with_the_stores_and_both_leaders()
    {
        await using var api = await StartAsync(postgres);
        await GiveNumbersAsync(api);

        (await Service(api, "017").TryApplyAsync(CancellationToken.None)).ShouldBeTrue();

        var structure = await StructureAsync(api);
        var district = structure.GetProperty("districts").EnumerateArray().Single();
        district.GetProperty("leaders").EnumerateArray().Select(l => l.GetProperty("id").GetGuid()).ShouldBe([Ben]);
        var region = structure.GetProperty("regions").EnumerateArray().Single();
        region.GetProperty("leaders").EnumerateArray().Select(l => l.GetProperty("id").GetGuid()).ShouldBe([Erika]);
        region.GetProperty("districtId").GetGuid().ShouldBe(district.GetProperty("id").GetGuid());
        var store = structure.GetProperty("stores").EnumerateArray().Single(s => s.GetProperty("storeId").GetGuid() == StoreA);
        store.GetProperty("regionId").GetGuid().ShouldBe(region.GetProperty("id").GetGuid());

        // the regional leader reaches the store, the district leader as well
        api.Grant(Erika, TestGrant.Everywhere(Portal.Core.PortalPrivileges.LogbookReview));
        using var me = await api.SendAsync(HttpMethod.Get, "/api/logbook/me", Erika);
        (await DataAsync(me)).GetProperty("stores").EnumerateArray().Select(s => s.GetProperty("storeId").GetGuid()).ShouldBe([StoreA]);
    }

    [Fact]
    public async Task Waits_while_a_person_or_a_store_is_not_there_yet_and_writes_nothing()
    {
        await using var api = await StartAsync(postgres);
        await GiveNumbersAsync(api);

        (await Service(api, "017", "999").TryApplyAsync(CancellationToken.None)).ShouldBeFalse(); // store 999 has not arrived

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        (await db.LogbookDistricts.AnyAsync()).ShouldBeFalse();
        (await db.LogbookStoreAssignments.AnyAsync()).ShouldBeFalse();

        (await Service(api, "017").TryApplyAsync(CancellationToken.None)).ShouldBeTrue(); // without that store it works
        (await db.LogbookDistricts.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Waits_without_the_personnel_numbers_of_the_leaders()
    {
        await using var api = await StartAsync(postgres); // the copies have no personnel numbers yet

        (await Service(api, "017").TryApplyAsync(CancellationToken.None)).ShouldBeFalse();
    }

    [Fact]
    public async Task Never_overwrites_a_hierarchy_that_somebody_maintains()
    {
        await using var api = await StartAsync(postgres);
        await GiveNumbersAsync(api);
        var own = new
        {
            districts = new object[] { new { id = Guid.NewGuid(), name = "Eigener Bezirk", leaderIds = new[] { Ben } } },
            regions = Array.Empty<object>(),
            stores = Array.Empty<object>(),
        };
        using (var put = await api.SendAsync(HttpMethod.Put, "/api/logbook/structure", Hanna, own))
        {
            put.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
        }

        (await Service(api, "017").TryApplyAsync(CancellationToken.None)).ShouldBeTrue(); // nothing more to do

        var districts = (await StructureAsync(api)).GetProperty("districts").EnumerateArray().ToList();
        districts.Single().GetProperty("name").GetString().ShouldBe("Eigener Bezirk");
        (await StructureAsync(api)).GetProperty("regions").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Is_only_active_with_stores_and_both_leaders_configured()
    {
        new LogbookDemoSeedOptions().IsActive.ShouldBeFalse();
        new LogbookDemoSeedOptions { StoreNumbers = ["F017"], RegionalLeader = "10051" }.IsActive.ShouldBeFalse();
        new LogbookDemoSeedOptions { StoreNumbers = ["F017"], RegionalLeader = "10051", DistrictLeader = "10052" }.IsActive.ShouldBeTrue();
        await Task.CompletedTask;
    }
}
