using Microsoft.Extensions.Options;

namespace Rollen_Rechte.Services;

/// <summary>Section "Locations": Verhalten des Filialabgleichs.</summary>
public class LocationSyncOptions
{
    public const string SectionName = "Locations";

    /// <summary>false: kein Filialabgleich beim Start (bleibt per "sync-stores" möglich).</summary>
    public bool SyncOnStartup { get; set; } = true;
}

/// <summary>
/// Einmaliger Filial-Abgleich beim Start, ohne den Start zu blockieren. Ein Fehler (BC-Anbindung oder BC nicht erreichbar) wird
/// nur protokolliert: der Dienst läuft mit den vorhandenen Locations weiter, "sync-stores" holt es nach.
/// Ohne Konfiguration der BC-Anbindung passiert nichts.
/// </summary>
public class BcStoreSyncStartup(IServiceScopeFactory scopeFactory, IOptions<BcGatewayOptions> gateway, IOptions<LocationSyncOptions> options, ILogger<BcStoreSyncStartup> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!gateway.Value.IsConfigured || !options.Value.SyncOnStartup) return;

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<BcStoreSync>().SyncAsync(stoppingToken);
            logger.LogInformation("BC-Filialabgleich: {Created} neu, {Renamed} umbenannt, {Skipped} übersprungen.",
                result.Created, result.Renamed, result.Skipped);
        }
        catch (BcGatewayUnavailableException ex)
        {
            // Erwartbar (BC oder die BC-Anbindung ist gerade nicht da): eine Zeile genügt, kein Stacktrace.
            logger.LogWarning("BC-Filialabgleich beim Start nicht möglich: {Message}", ex.Message);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "BC-Filialabgleich beim Start fehlgeschlagen.");
        }
    }
}
