using System.Net;
using HrTool_API.Authentication;
using HrTool_API.Domain.Entities;
using HrTool_API.Services;
using HrTool_API.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HrTool_API.Tests.Functional;

/// <summary>Der Stamm der Arbeitsorte (Export „Arbeitsorte“ aus BC): Inhalt, Anlegen, Auflösen der Filialen über die Store-Nr., Routen und Scope.</summary>
public class WorkplaceTests(ApiFactory factory) : ApiTestBase(factory)
{
    private static string Code() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private async Task<string> AdminAsync() => (await UserWithPrivilegesAsync([.. PrivilegeCatalog.All])).Token;

    private Task<Workplace> InsertWorkplaceAsync(string? storeCode, Guid? locationId = null) =>
        Factory.WithDbAsync(async (db, _) =>
        {
            var w = new Workplace { Code = Code(), Name = "Test", StoreCode = storeCode, LocationId = locationId };
            db.Workplaces.Add(w);
            await db.SaveChangesAsync();
            return w;
        });

    private Task<Workplace?> ReloadAsync(Guid id) => Factory.WithDbAsync(async (db, _) => await db.Workplaces.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id));

    // --- Stamm ------------------------------------------------------------------------------------------------

    [Fact]
    public void TheSeed_HasAll42WorkplacesOfTheExport_WithTheirMasterData()
    {
        var seed = ReferenceDataMaintenance.ReadSeed();

        Assert.Equal(42, seed.Count);
        Assert.Equal(seed.Count, seed.Select(s => s.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var ah = seed.Single(s => s.Code == "AH");
        Assert.Equal(("Augsburg Hauptbahnhof", "0012", "12", "REGION_4"), (ah.Name, ah.StoreCode, ah.CostCenterCode, ah.RegionCode));
        Assert.Equal("95", seed.Single(s => s.Code == "HO").StoreCode);
        Assert.Equal(4, seed.Single(s => s.Code == "HO").VacationThreshold);
        Assert.All(seed.Where(s => s.Code != "HO"), s => Assert.Equal(1, s.VacationThreshold));
        Assert.All(seed, s => Assert.True(s.TimeAccountActive));
        Assert.Contains(seed, s => s.Code == "FÜS");   // Umlaute im Kürzel bleiben erhalten
        Assert.Equal("Kaufbeuren", seed.Single(s => s.Code == "KF").Name); // im Export steht ein Leerzeichen dahinter
        // Ohne Filiale: Berufsschule und Samurai Bar.
        Assert.Equal(["BS", "SABA"], seed.Where(s => s.StoreCode is null).Select(s => s.Code).Order());
    }

    [Fact]
    public async Task Seeding_CreatesTheMissingWorkplaces_AndIsRepeatable_WithoutOverwritingChanges()
    {
        await Factory.WithDbAsync((db, _) => ReferenceDataMaintenance.SeedWorkplacesAsync(db, default));
        var before = await Factory.WithDbAsync((db, _) => db.Workplaces.CountAsync());
        Assert.True(before >= 42);

        // Eine Änderung (z. B. aus BC) bleibt beim nächsten Durchlauf erhalten.
        await Factory.WithDbAsync(async (db, _) =>
        {
            (await db.Workplaces.SingleAsync(w => w.Code == "AB")).Name = "Neuer Name";
            await db.SaveChangesAsync();
        });
        var created = await Factory.WithDbAsync((db, _) => ReferenceDataMaintenance.SeedWorkplacesAsync(db, default));

        Assert.Equal(0, created);
        Assert.Equal(before, await Factory.WithDbAsync((db, _) => db.Workplaces.CountAsync()));
        Assert.Equal("Neuer Name", await Factory.WithDbAsync((db, _) => db.Workplaces.Where(w => w.Code == "AB").Select(w => w.Name).SingleAsync()));
    }

    // --- Filialen auflösen --------------------------------------------------------------------------------------

    [Fact]
    public async Task Resolving_MatchesTheStoreCode_IgnoringLeadingZeros()
    {
        var store = Random.Shared.Next(100000, 999999).ToString();
        var location = Factory.Locations.Add("Filiale", "000" + store);   // Rollen-Rechte: mit Nullen
        var workplace = await InsertWorkplaceAsync(store);                 // BC-Arbeitsorte: ohne (wie „95“ bei HO)
        var noStore = await InsertWorkplaceAsync(null);

        var changed = await Factory.WithDbAsync((db, _) => ReferenceDataMaintenance.ResolveLocationsAsync(db, Factory.Locations, default));

        Assert.True(changed >= 1);
        Assert.Equal(location.Id, (await ReloadAsync(workplace.Id))!.LocationId);
        Assert.Null((await ReloadAsync(noStore.Id))!.LocationId);
    }

    [Fact]
    public async Task Resolving_DoesNotGiveOneLocationToTwoWorkplaces_AndKeepsAKnownLocationWhenNothingMatches()
    {
        var store = Random.Shared.Next(100000, 999999).ToString();
        var location = Factory.Locations.Add("Filiale", store);
        var first = await InsertWorkplaceAsync(store);
        var second = await InsertWorkplaceAsync("0" + store);   // gleiche Store-Nr. ohne Nullen: gleiche Filiale
        var kept = await InsertWorkplaceAsync("99999999", locationId: Guid.NewGuid()); // keine Filiale mit dieser Store-Nr.: Wert bleibt

        await Factory.WithDbAsync((db, _) => ReferenceDataMaintenance.ResolveLocationsAsync(db, Factory.Locations, default));

        var locations = new[] { (await ReloadAsync(first.Id))!.LocationId, (await ReloadAsync(second.Id))!.LocationId };
        Assert.Single(locations, l => l == location.Id);   // genau einer bekommt sie
        Assert.NotNull((await ReloadAsync(kept.Id))!.LocationId);
    }

    [Fact]
    public async Task Resolving_WhenRollenRechteIsDown_FailsWithoutChangingAnything()
    {
        var store = Random.Shared.Next(100000, 999999).ToString();
        var workplace = await InsertWorkplaceAsync(store);
        Factory.Locations.Add("Filiale", store);
        Factory.Locations.Unavailable = true;

        var ex = await Assert.ThrowsAsync<HrTool_API.Common.Api.ApiException>(() =>
            Factory.WithDbAsync((db, _) => ReferenceDataMaintenance.ResolveLocationsAsync(db, Factory.Locations, default)));

        Assert.Equal(503, ex.StatusCode);
        Assert.Null((await ReloadAsync(workplace.Id))!.LocationId);
    }

    // --- Routen -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task List_ReturnsTheWorkplaces_WithMasterData()
    {
        await Factory.WithDbAsync((db, _) => ReferenceDataMaintenance.SeedWorkplacesAsync(db, default));
        var (_, token) = await UserWithPrivilegesAsync("workplace:read");

        var items = (await DataAsync(await GetAsync("/api/workplaces", token))).EnumerateArray().ToList();

        var ah = items.Single(i => i.GetProperty("code").GetString() == "AH");
        Assert.Equal("Augsburg Hauptbahnhof", ah.GetProperty("name").GetString());
        Assert.Equal("0012", ah.GetProperty("storeCode").GetString());
        Assert.Equal("REGION_4", ah.GetProperty("regionCode").GetString());
    }

    [Fact]
    public async Task Update_AssignsALocation_ButOnlyAKnownOneAndOnlyOncePerLocation()
    {
        var (_, token) = await UserWithPrivilegesAsync("workplace:update", "workplace:read");
        var location = CreateLocation();
        var a = await InsertWorkplaceAsync(null);
        var b = await InsertWorkplaceAsync(null);

        var ok = await PatchAsync($"/api/workplaces/{a.Id}", new { locationId = location.Id }, token);
        var twice = await PatchAsync($"/api/workplaces/{b.Id}", new { locationId = location.Id }, token);
        var unknown = await PatchAsync($"/api/workplaces/{b.Id}", new { locationId = Guid.NewGuid() }, token);
        var missing = await PatchAsync($"/api/workplaces/{Guid.NewGuid()}", new { locationId = location.Id }, token);

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(location.Id, (await DataAsync(ok)).GetProperty("locationId").GetGuid());
        Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    // --- Scope: Arbeitsorte ohne Filiale ----------------------------------------------------------------------

    [Fact]
    public async Task APlanOfAWorkplaceWithoutALocation_IsOnlyForUnrestrictedUsers()
    {
        var admin = await AdminAsync();
        var withoutLocation = await WorkplaceWithoutLocationAsync();
        var (_, scoped) = await ScopedUserAsync(["schedule:read", "schedule:create"], [CreateLocation().Id]);
        var body = new { number = Unique("DP"), workplaceId = withoutLocation.Id, year = 2090, month = 1 };

        var denied = await PostAsync("/api/schedules", body, scoped);
        var created = await PostAsync("/api/schedules", body, admin);
        var planId = (await DataAsync(created)).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(AuthMessages.Create, (await JsonAsync(denied)).GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, (await DataAsync(created)).GetProperty("locationId").ValueKind);
        Assert.Equal(withoutLocation.Code, (await DataAsync(created)).GetProperty("workplaceCode").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync($"/api/schedules/{planId}", scoped)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync($"/api/schedules/{planId}", admin)).StatusCode);
    }

}
