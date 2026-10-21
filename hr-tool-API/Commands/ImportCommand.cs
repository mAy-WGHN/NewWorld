using HrTool_API.Data;
using HrTool_API.Services.Import;

namespace HrTool_API.Commands;

/// <summary>
/// <c>dotnet run --project HrTool-API.csproj -- import [ordner] [--dry-run]</c> (oder <c>make import</c>): liest die BC-Exporte (.xlsx) in die Datenbank (siehe <see cref="SpreadsheetImport"/>).
/// Der Ordner ist standardmäßig <c>docs/spreadsheets</c>. Mit <c>--dry-run</c> läuft alles, wird aber zurückgerollt. Die Exporte enthalten Personaldaten: Sie bleiben außerhalb von Git,
/// und die Ausgabe nennt nur Personalnummern und Codes, nie Namen.
/// </summary>
public static class ImportCommand
{
    public const string Name = "import";

    public static async Task<int> RunAsync(IServiceProvider services, string[] args, TextWriter output)
    {
        var dryRun = args.Contains("--dry-run");
        var folder = args.FirstOrDefault(a => !a.StartsWith("--")) ?? Path.Combine("docs", "spreadsheets");

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HrToolDbContext>();
        try
        {
            if (!await db.Database.CanConnectAsync())
            {
                await output.WriteLineAsync("Keine Verbindung zur Datenbank (ConnectionStrings__Default, siehe .env.example).");
                return 1;
            }

            var report = await new SpreadsheetImport(db).RunAsync(folder, dryRun, CancellationToken.None);
            await output.WriteLineAsync(dryRun ? "Probelauf (nichts gespeichert):" : "Import abgeschlossen:");
            foreach (var (section, (created, updated, skipped)) in report.Sections)
                await output.WriteLineAsync($"  {section}: {created} neu, {updated} aktualisiert, {skipped} übersprungen");
            foreach (var warning in report.Warnings.Take(20)) await output.WriteLineAsync($"  Warnung: {warning}");
            if (report.Warnings.Count > 20) await output.WriteLineAsync($"  … und {report.Warnings.Count - 20} weitere Warnungen.");
            return 0;
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or InvalidDataException or Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            // Nur der Fehlertyp und die Meldung: Fehlertexte der Datenbank können Werte enthalten, deshalb bei Datenbankfehlern nicht die Meldung.
            await output.WriteLineAsync(ex is Microsoft.EntityFrameworkCore.DbUpdateException
                ? "Import fehlgeschlagen (Datenbankfehler); es wurde nichts gespeichert. Läuft das Schema (make migrate)?"
                : $"Import fehlgeschlagen: {ex.Message}");
            return 1;
        }
    }
}
