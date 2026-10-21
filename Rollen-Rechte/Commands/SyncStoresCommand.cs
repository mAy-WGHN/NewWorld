using Rollen_Rechte.Services;
using Microsoft.Extensions.Options;

namespace Rollen_Rechte.Commands;

/// <summary>"dotnet Rollen-Rechte.dll sync-stores": gleicht die Filialen über die BC-Anbindung mit Business Central ab (siehe <see cref="BcStoreSync"/>).</summary>
public static class SyncStoresCommand
{
    public const string Name = "sync-stores";

    public static async Task<int> RunAsync(IServiceProvider services, TextWriter output)
    {
        using var scope = services.CreateScope();
        if (!scope.ServiceProvider.GetRequiredService<IOptions<BcGatewayOptions>>().Value.IsConfigured)
        {
            await output.WriteLineAsync("BcGateway__BaseUrl und BcGateway__ApiKey sind nicht konfiguriert (siehe .env.example).");
            return 1;
        }

        try
        {
            var result = await scope.ServiceProvider.GetRequiredService<BcStoreSync>().SyncAsync(CancellationToken.None);
            await output.WriteLineAsync($"Filialen: {result.Created} neu, {result.Renamed} umbenannt, {result.Skipped} übersprungen.");
            return 0;
        }
        catch (BcGatewayUnavailableException ex)
        {
            await output.WriteLineAsync($"BC-Abgleich fehlgeschlagen: {ex.Message}");
            return 1;
        }
    }
}
