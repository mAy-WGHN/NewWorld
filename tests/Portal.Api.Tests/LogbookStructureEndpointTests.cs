using System.Net;
using System.Text.Json;
using BcIntegration.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Portal.Core;
using Portal.Infrastructure;
using Shared.Testing;
using static Portal.Api.Tests.PortalApi;

namespace Portal.Api.Tests;

/// <summary>
/// The hierarchy of the Berichtsheft through the real API and database (decision 49): maintained as a whole, validated, and "which stores do I reach".
/// Clock: 2026-10-23, 12:00 UTC.
/// </summary>
public sealed class LogbookStructureEndpointTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly Guid DistrictSouth = Guid.Parse("00000000-0000-0000-0000-0000000000d1");
    private static readonly Guid DistrictNorth = Guid.Parse("00000000-0000-0000-0000-0000000000d2");
    private static readonly Guid RegionAugsburg = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid RegionMunich = Guid.Parse("00000000-0000-0000-0000-0000000000a2");

    // Erika is regional leader of Augsburg, Revisor of Munich, Ben is district leader of the South, It leads nothing.
    private static readonly Guid RegionalAugsburg = Erika;
    private static readonly Guid RegionalMunich = Revisor;
    private static readonly Guid DistrictLeaderSouth = Ben;

    /// <summary>South: Augsburg (store A, B; RL Erika), Munich (store C; RL Rita), store D directly (closed store, kept for the test); BL Ben. North: empty, no leader.</summary>
    private static object Structure() => new
    {
        districts = new object[]
        {
            new { id = DistrictSouth, name = "Süd", leaderIds = new[] { DistrictLeaderSouth } },
            new { id = DistrictNorth, name = "Nord", leaderIds = Array.Empty<Guid>() },
        },
        regions = new object[]
        {
            new { id = RegionAugsburg, districtId = DistrictSouth, name = "Augsburg", leaderIds = new[] { RegionalAugsburg } },
            new { id = RegionMunich, districtId = DistrictSouth, name = "München", leaderIds = new[] { RegionalMunich } },
        },
        stores = new object[]
        {
            new { storeId = StoreA, districtId = DistrictSouth, regionId = (Guid?)RegionAugsburg },
            new { storeId = StoreB, districtId = DistrictSouth, regionId = (Guid?)RegionAugsburg },
            new { storeId = StoreC, districtId = DistrictSouth, regionId = (Guid?)RegionMunich },
        },
    };

    private static async Task<HttpResponseMessage> PutAsync(PortalApi api, object body, Guid? person = null) =>
        await api.SendAsync(HttpMethod.Put, "/api/logbook/structure", person ?? Hanna, body);

    private static async Task SetUpAsync(PortalApi api)
    {
        using var response = await PutAsync(api, Structure());
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        api.Grant(RegionalAugsburg, TestGrant.Everywhere(PortalPrivileges.LogbookReview));
        api.Grant(RegionalMunich, TestGrant.Everywhere(PortalPrivileges.LogbookReview));
        api.Grant(DistrictLeaderSouth, TestGrant.Everywhere(PortalPrivileges.LogbookReview));
        api.Grant(It, TestGrant.Everywhere(PortalPrivileges.LogbookReview));
    }

    private static async Task<JsonElement> GetStructureAsync(PortalApi api)
    {
        using var response = await api.SendAsync(HttpMethod.Get, "/api/logbook/structure", Hanna);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await DataAsync(response);
    }

    private static async Task<JsonElement> MeAsync(PortalApi api, Guid person)
    {
        using var response = await api.SendAsync(HttpMethod.Get, "/api/logbook/me", person);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await DataAsync(response);
    }

    private static List<Guid> StoreIds(JsonElement me) => [.. me.GetProperty("stores").EnumerateArray().Select(s => s.GetProperty("storeId").GetGuid())];

    // ------------------------------------------------------------------ maintaining

    [Fact]
    public async Task Nothing_is_set_up_at_first_and_the_structure_is_saved_and_read_back_with_names()
    {
        await using var api = await StartAsync(postgres);
        var empty = await GetStructureAsync(api);
        empty.GetProperty("districts").GetArrayLength().ShouldBe(0);

        await SetUpAsync(api);
        var structure = await GetStructureAsync(api);

        var south = structure.GetProperty("districts").EnumerateArray().Single(d => d.GetProperty("id").GetGuid() == DistrictSouth);
        south.GetProperty("name").GetString().ShouldBe("Süd");
        south.GetProperty("leaders").EnumerateArray().Select(l => l.GetProperty("name").GetString()).ShouldBe(["Ben Beispiel"]);
        var augsburg = structure.GetProperty("regions").EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == RegionAugsburg);
        augsburg.GetProperty("districtId").GetGuid().ShouldBe(DistrictSouth);
        augsburg.GetProperty("leaders").EnumerateArray().Select(l => l.GetProperty("id").GetGuid()).ShouldBe([RegionalAugsburg]);
        var stores = structure.GetProperty("stores").EnumerateArray().ToList();
        stores.Select(s => s.GetProperty("storeId").GetGuid()).ShouldBe([StoreA, StoreB, StoreC], ignoreOrder: true);
        stores.Single(s => s.GetProperty("storeId").GetGuid() == StoreA).GetProperty("storeName").GetString().ShouldBe("Store A");
        stores.Single(s => s.GetProperty("storeId").GetGuid() == StoreA).GetProperty("storeNumber").GetString().ShouldBe("017");
    }

    [Fact]
    public async Task Saving_again_changes_only_what_differs_and_removes_what_is_gone_with_its_leaders()
    {
        await using var api = await StartAsync(postgres);
        await SetUpAsync(api);

        // Munich is dropped, Augsburg gets a new name and loses its leader, store C moves to the district directly.
        var changed = new
        {
            districts = new object[] { new { id = DistrictSouth, name = "Süd", leaderIds = new[] { DistrictLeaderSouth } } },
            regions = new object[] { new { id = RegionAugsburg, districtId = DistrictSouth, name = "Augsburg Stadt", leaderIds = Array.Empty<Guid>() } },
            stores = new object[]
            {
                new { storeId = StoreA, districtId = DistrictSouth, regionId = (Guid?)RegionAugsburg },
                new { storeId = StoreC, districtId = DistrictSouth, regionId = (Guid?)null },
            },
        };
        (await PutAsync(api, changed)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var structure = await GetStructureAsync(api);
        structure.GetProperty("districts").GetArrayLength().ShouldBe(1);
        var region = structure.GetProperty("regions").EnumerateArray().Single();
        (region.GetProperty("id").GetGuid(), region.GetProperty("name").GetString()).ShouldBe((RegionAugsburg, "Augsburg Stadt"));
        region.GetProperty("leaders").GetArrayLength().ShouldBe(0);
        structure.GetProperty("stores").EnumerateArray().Select(s => s.GetProperty("storeId").GetGuid()).ShouldBe([StoreA, StoreC], ignoreOrder: true);

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        (await db.LogbookRegionLeaders.CountAsync()).ShouldBe(0); // no leftovers of the removed region
        (await db.LogbookStoreAssignments.SingleAsync(s => s.StoreId == StoreC)).RegionId.ShouldBeNull();
    }

    [Fact]
    public async Task A_structure_with_a_wrong_part_is_refused_as_a_whole_and_nothing_changes()
    {
        await using var api = await StartAsync(postgres);
        await SetUpAsync(api);
        var broken = new
        {
            districts = new object[] { new { id = DistrictSouth, name = " ", leaderIds = Array.Empty<Guid>() } },
            regions = Array.Empty<object>(),
            stores = Array.Empty<object>(),
        };

        using var response = await PutAsync(api, broken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ErrorAsync(response)).GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("field").GetString()).ShouldContain("districts");
        (await GetStructureAsync(api)).GetProperty("districts").GetArrayLength().ShouldBe(2); // as before
    }

    [Fact]
    public async Task Unknown_people_and_unknown_stores_and_missing_ids_are_refused_with_the_field()
    {
        await using var api = await StartAsync(postgres);
        var unknownLeader = new
        {
            districts = new object[] { new { id = DistrictSouth, name = "Süd", leaderIds = new[] { Guid.NewGuid() } } }, regions = Array.Empty<object>(), stores = Array.Empty<object>(),
        };
        var unknownStore = new
        {
            districts = new object[] { new { id = DistrictSouth, name = "Süd", leaderIds = Array.Empty<Guid>() } }, regions = Array.Empty<object>(),
            stores = new object[] { new { storeId = Guid.NewGuid(), districtId = DistrictSouth, regionId = (Guid?)null } },
        };
        var noId = new { districts = new object[] { new { id = Guid.Empty, name = "Süd", leaderIds = Array.Empty<Guid>() } }, regions = Array.Empty<object>(), stores = Array.Empty<object>() };

        List<(object Body, string Field)> cases = [(unknownLeader, "districts"), (unknownStore, "stores"), (noId, "districts")];
        foreach (var (body, field) in cases)
        {
            using var response = await PutAsync(api, body);
            response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
            (await ErrorAsync(response)).GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("field").GetString()).ShouldContain(field);
        }

        (await PutAsync(api, new { districts = (object?)null, regions = Array.Empty<object>(), stores = Array.Empty<object>() })).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Only_people_with_the_privilege_read_and_maintain_the_structure()
    {
        await using var api = await StartAsync(postgres);
        api.Grant(Erika, TestGrant.Everywhere(PortalPrivileges.LogbookReview), TestGrant.Everywhere(PortalPrivileges.LogbookWrite)); // other privileges do not help

        foreach (var person in new Guid?[] { Erika, Ben, null })
        {
            var expected = person is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
            (await api.SendAsync(HttpMethod.Get, "/api/logbook/structure", person)).StatusCode.ShouldBe(expected);
            (await api.SendAsync(HttpMethod.Put, "/api/logbook/structure", person, Structure())).StatusCode.ShouldBe(expected);
        }

        api.Grant(Ben, TestGrant.Everywhere(PortalPrivileges.LogbookManageStructure));
        (await api.SendAsync(HttpMethod.Get, "/api/logbook/structure", Ben)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PutAsync(api, Structure(), Ben)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ------------------------------------------------------------------ which stores do I reach

    [Fact]
    public async Task A_regional_leader_reaches_the_region_and_a_district_leader_the_whole_district()
    {
        await using var api = await StartAsync(postgres);
        await SetUpAsync(api);

        var regional = await MeAsync(api, RegionalAugsburg);
        regional.GetProperty("level").GetString().ShouldBe("regional");
        StoreIds(regional).ShouldBe([StoreA, StoreB], ignoreOrder: true);
        StoreIds(await MeAsync(api, RegionalMunich)).ShouldBe([StoreC]);

        var district = await MeAsync(api, DistrictLeaderSouth);
        district.GetProperty("level").GetString().ShouldBe("district");
        StoreIds(district).ShouldBe([StoreA, StoreB, StoreC], ignoreOrder: true);
        district.GetProperty("stores").EnumerateArray().First().GetProperty("storeName").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task A_person_with_the_privilege_who_leads_nothing_reaches_nothing_and_without_the_privilege_there_is_no_answer()
    {
        await using var api = await StartAsync(postgres);
        await SetUpAsync(api);

        var nobody = await MeAsync(api, It);
        nobody.GetProperty("level").GetString().ShouldBe("none");
        StoreIds(nobody).ShouldBeEmpty();

        (await api.SendAsync(HttpMethod.Get, "/api/logbook/me", Hanna)).StatusCode.ShouldBe(HttpStatusCode.OK); // Hanna has everything but leads nothing
        api.Grant(Ben, TestGrant.Everywhere(PortalPrivileges.FaqRead));
        (await api.SendAsync(HttpMethod.Get, "/api/logbook/me", Ben)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await api.SendAsync(HttpMethod.Get, "/api/logbook/me", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ------------------------------------------------------------------ erasure

    [Fact]
    public async Task When_a_person_is_erased_they_no_longer_lead_anything_and_the_rest_of_the_structure_stays()
    {
        await using var api = await StartAsync(postgres);
        await SetUpAsync(api);

        await api.DeliverAsync(new EmployeeDataErasureRequested(Guid.NewGuid(), Guid.NewGuid(), Start, RegionalAugsburg));

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        (await db.LogbookRegionLeaders.AnyAsync(l => l.EmployeeId == RegionalAugsburg)).ShouldBeFalse();
        (await db.LogbookRegionLeaders.AnyAsync(l => l.EmployeeId == RegionalMunich)).ShouldBeTrue();
        (await db.LogbookRegions.CountAsync()).ShouldBe(2);
        (await db.LogbookStoreAssignments.CountAsync()).ShouldBe(3);
    }
}
