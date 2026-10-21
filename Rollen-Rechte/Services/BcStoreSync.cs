using Microsoft.EntityFrameworkCore;
using Rollen_Rechte.Data;
using Rollen_Rechte.Domain.Entities;

namespace Rollen_Rechte.Services;

public record StoreSyncResult(int Created, int Renamed, int Skipped);

/// <summary>
/// Gleicht die BC-Filialen (über die BC-Anbindung) mit den Locations dieses Dienstes ab: Store-Nr. = <see cref="Location.StoreCode"/>,
/// Name = <see cref="Location.Name"/>. BC ist für den Namen führend. Neue Filialen werden angelegt, bekannte umbenannt. Archivierte Locations
/// und Locations ohne BC-Gegenstück (manuell angelegt) bleiben unangetastet — es wird nie archiviert oder gelöscht.
/// Die Filialen sind die Stammdaten für Scopes und Heimatfiliale; Portal und HR-Tool bekommen sie von hier.
/// </summary>
public class BcStoreSync(IBcGateway gateway, AccessDbContext db, ILogger<BcStoreSync> logger)
{
    // Spaltenlängen der Tabelle "locations" (siehe AccessDbContext).
    private const int MaxStoreCodeLength = 10;
    private const int MaxNameLength = 255;

    public async Task<StoreSyncResult> SyncAsync(CancellationToken ct)
    {
        var bcStores = await gateway.GetStoresAsync(ct);

        var existing = await db.Locations.ToDictionaryAsync(l => l.StoreCode, ct);
        int created = 0, renamed = 0, skipped = 0;

        foreach (var store in bcStores)
        {
            var storeCode = store.StoreNo.Trim();
            var name = store.Name.Trim();
            if (storeCode.Length is 0 or > MaxStoreCodeLength || name.Length is 0 or > MaxNameLength)
            {
                skipped++;
                logger.LogWarning("BC-Filiale '{StoreCode}' übersprungen (leerer/zu langer Wert).", storeCode);
                continue;
            }

            if (!existing.TryGetValue(storeCode, out var location))
            {
                location = new Location { StoreCode = storeCode, Name = name };
                db.Locations.Add(location);
                existing[storeCode] = location;
                created++;
            }
            else if (location.Name != name)
            {
                location.Name = name;
                renamed++;
            }
        }

        await db.SaveChangesAsync(ct);
        return new StoreSyncResult(created, renamed, skipped);
    }
}
