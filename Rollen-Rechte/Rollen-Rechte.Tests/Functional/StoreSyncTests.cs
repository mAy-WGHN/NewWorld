using Rollen_Rechte.Services;
using Rollen_Rechte.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Rollen_Rechte.Tests.Functional;

public class StoreSyncTests(ApiFactory factory) : ApiTestBase(factory)
{
    private Task<StoreSyncResult> SyncAsync() =>
        Factory.WithDbAsync((_, sp) => sp.GetRequiredService<BcStoreSync>().SyncAsync(CancellationToken.None));

    [Fact]
    public async Task Sync_CreatesNewStores_AndRenamesKnownOnes()
    {
        var newCode = Random.Shared.Next(100_000, 999_999).ToString();
        var known = await CreateLocationAsync(name: "Alter Name");
        Factory.Bc.Stores.Add((newCode, "Neue Filiale"));
        Factory.Bc.Stores.Add((known.StoreCode, "Neuer Name"));

        var result = await SyncAsync();

        Assert.True(result.Created >= 1);
        Assert.True(result.Renamed >= 1);
        var locations = await QueryDbAsync(db => db.Locations.AsNoTracking()
            .Where(l => l.StoreCode == newCode || l.StoreCode == known.StoreCode).ToListAsync());
        Assert.Equal("Neue Filiale", locations.Single(l => l.StoreCode == newCode).Name);
        Assert.Equal("Neuer Name", locations.Single(l => l.StoreCode == known.StoreCode).Name);
    }

    [Fact]
    public async Task Sync_KeepsArchivedFlagAndLocationsUnknownToBc()
    {
        var archived = await CreateLocationAsync(archived: true);
        var manual = await CreateLocationAsync(name: "Nur in IRS");
        Factory.Bc.Stores.Add((archived.StoreCode, "Umbenannt in BC"));

        await SyncAsync();

        var locations = await QueryDbAsync(db => db.Locations.AsNoTracking()
            .Where(l => l.Id == archived.Id || l.Id == manual.Id).ToListAsync());
        var reloadedArchived = locations.Single(l => l.Id == archived.Id);
        Assert.True(reloadedArchived.Archived);
        Assert.Equal("Umbenannt in BC", reloadedArchived.Name);
        var reloadedManual = locations.Single(l => l.Id == manual.Id);
        Assert.False(reloadedManual.Archived);
        Assert.Equal("Nur in IRS", reloadedManual.Name);
    }

    [Fact]
    public async Task Sync_SkipsStoresThatDoNotFitTheSchema_AndIsIdempotent()
    {
        var code = Random.Shared.Next(100_000, 999_999).ToString();
        Factory.Bc.Stores.Add((code, "Passt"));
        Factory.Bc.Stores.Add(("12345678901", "Store-Nr. zu lang"));
        Factory.Bc.Stores.Add(("", "Ohne Nummer"));

        var first = await SyncAsync();
        var second = await SyncAsync();

        Assert.Equal(1, first.Created);
        Assert.Equal(2, first.Skipped);
        Assert.Equal(0, second.Created);
        Assert.Equal(0, second.Renamed);
    }
}
