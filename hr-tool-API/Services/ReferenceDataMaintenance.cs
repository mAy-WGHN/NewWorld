using System.Reflection;
using System.Text.Json;
using HrTool_API.Data;
using HrTool_API.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HrTool_API.Services;

/// <summary>Section "ReferenceData": <c>MaintenanceEnabled</c> (Standard true) schaltet das Anlegen der Stammdaten (Arbeitsorte, Nichtverfügbarkeitsarten) und das Auflösen der Filialen ab (z. B. in Tests).</summary>
public class ReferenceDataOptions
{
    public const string SectionName = "ReferenceData";

    public bool MaintenanceEnabled { get; set; } = true;
}

/// <summary>Eine Nichtverfügbarkeitsart (<c>Data/Seed/absence-types.json</c>, Export „Nichtverfügbarkeitsarten“ aus BC, 24). Genehmigt werden muss nur <c>URLAUB</c>; die Angabe steht nicht in BC.</summary>
public sealed record AbsenceTypeSeedEntry(string Code, string Description, bool PaidTime, string? ScheduleSymbol, bool RequiresApproval);

/// <summary>Ein Arbeitsort des Stamms „Arbeitsorte“ aus BC (<c>Data/Seed/workplaces.json</c>).</summary>
public sealed record WorkplaceSeedEntry(
    string Code, string Name, string? StoreCode, string? CostCenterCode, string? RegionCode, bool TimeAccountActive, int VacationThreshold);

/// <summary>
/// Hält die Arbeitsorte bis zur echten BC-Anbindung selbst aktuell:
/// <list type="number">
/// <item><b>Anlegen:</b> Fehlende Arbeitsorte werden aus dem mitgelieferten Stamm (<c>Data/Seed/workplaces.json</c>, Export „Arbeitsorte“ aus BC, 42 Stück) angelegt. Vorhandene bleiben unangetastet
///   (spätere Änderungen aus BC oder von Hand werden nicht überschrieben).</item>
/// <item><b>Auflösen:</b> Die Filiale eines Arbeitsorts (<see cref="Workplace.LocationId"/>) ergibt sich aus der Store-Nr.: Sie wird mit dem <c>store_code</c> der Filialen von Rollen-Rechte verglichen
///   (Nullen vorn zählen nicht: <c>0012</c> = <c>12</c>). Das läuft regelmäßig, weil Rollen-Rechte beim Start fehlen oder die Filialen erst später anlegen kann.</item>
/// </list>
/// Läuft im Hintergrund und versucht es wieder: Das HR-Tool startet auch ohne Datenbankschema und ohne Rollen-Rechte.
/// </summary>
public class ReferenceDataMaintenance(IServiceScopeFactory scopes, ILocationDirectory locations, Microsoft.Extensions.Options.IOptions<ReferenceDataOptions> options, ILogger<ReferenceDataMaintenance> logger) : BackgroundService
{
    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ResolveEvery = TimeSpan.FromMinutes(10);
    private const string ResourceName = "HrTool_API.Data.Seed.workplaces.json";
    private const string AbsenceTypesResource = "HrTool_API.Data.Seed.absence-types.json";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.MaintenanceEnabled) return;

        var seeded = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = ResolveEvery;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<HrToolDbContext>();
                if (!seeded)
                {
                    var created = await SeedWorkplacesAsync(db, stoppingToken) + await SeedAbsenceTypesAsync(db, stoppingToken);
                    seeded = true;
                    logger.LogInformation("Stammdaten geprüft: {Count} neu angelegt.", created);
                }

                var resolved = await ResolveLocationsAsync(db, locations, stoppingToken);
                if (resolved > 0) logger.LogInformation("Filialen für {Count} Arbeitsorte aufgelöst.", resolved);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Schema noch nicht angewendet oder Rollen-Rechte nicht erreichbar: später noch einmal.
                logger.LogWarning("Arbeitsorte konnten nicht gepflegt werden ({ErrorType}); neuer Versuch in {Seconds} s.", ex.GetType().Name, RetryAfter.TotalSeconds);
                delay = RetryAfter;
            }

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public static IReadOnlyList<WorkplaceSeedEntry> ReadSeed()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
                           ?? throw new InvalidOperationException($"Die Ressource {ResourceName} fehlt.");
        return JsonSerializer.Deserialize<List<WorkplaceSeedEntry>>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))
               ?? throw new InvalidOperationException("Der Stamm der Arbeitsorte ist leer oder unlesbar.");
    }

    /// <summary>Legt die Arbeitsorte an, die es noch nicht gibt (nach <c>Code</c>); liefert die Anzahl der neuen.</summary>
    public static async Task<int> SeedWorkplacesAsync(HrToolDbContext db, CancellationToken ct)
    {
        var existing = (await db.Workplaces.Select(w => w.Code).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = ReadSeed().Where(s => !existing.Contains(s.Code)).ToList();
        foreach (var s in missing)
        {
            db.Workplaces.Add(new Workplace
            {
                Code = s.Code, Name = s.Name, StoreCode = s.StoreCode, CostCenterCode = s.CostCenterCode, RegionCode = s.RegionCode,
                TimeAccountActive = s.TimeAccountActive, VacationThreshold = s.VacationThreshold
            });
        }

        await db.SaveChangesAsync(ct);
        return missing.Count;
    }

    public static IReadOnlyList<AbsenceTypeSeedEntry> ReadAbsenceTypeSeed()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(AbsenceTypesResource)
                           ?? throw new InvalidOperationException($"Die Ressource {AbsenceTypesResource} fehlt.");
        return JsonSerializer.Deserialize<List<AbsenceTypeSeedEntry>>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))
               ?? throw new InvalidOperationException("Die Nichtverfügbarkeitsarten sind leer oder unlesbar.");
    }

    /// <summary>Legt die Nichtverfügbarkeitsarten an, die es noch nicht gibt (nach <c>Code</c>); vorhandene bleiben unangetastet. Liefert die Anzahl der neuen.</summary>
    public static async Task<int> SeedAbsenceTypesAsync(HrToolDbContext db, CancellationToken ct)
    {
        var existing = (await db.AbsenceTypes.Select(t => t.Code).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = ReadAbsenceTypeSeed().Where(s => !existing.Contains(s.Code)).ToList();
        foreach (var s in missing)
        {
            db.AbsenceTypes.Add(new AbsenceType
            {
                Code = s.Code, Description = s.Description, PaidTime = s.PaidTime, ScheduleSymbol = s.ScheduleSymbol, RequiresApproval = s.RequiresApproval
            });
        }

        await db.SaveChangesAsync(ct);
        return missing.Count;
    }

    /// <summary>
    /// Setzt die Filiale der Arbeitsorte anhand der Store-Nr.; liefert die Anzahl der geänderten. Eine Filiale, die ein anderer Arbeitsort schon hat, wird nicht doppelt vergeben;
    /// ein Arbeitsort ohne Treffer behält seinen bisherigen Wert (Rollen-Rechte kann gerade unvollständig sein).
    /// </summary>
    /// <exception cref="Common.Api.ApiException">503, wenn Rollen-Rechte nicht erreichbar ist und es noch keine Filialliste gibt.</exception>
    public static async Task<int> ResolveLocationsAsync(HrToolDbContext db, ILocationDirectory locations, CancellationToken ct)
    {
        var all = await locations.GetAllAsync(ct);
        // Je Store-Nr. (ohne führende Nullen) die Filiale; bei Doppelten die nicht archivierte.
        var byStore = all.Values.Where(l => !string.IsNullOrWhiteSpace(l.StoreCode))
            .GroupBy(l => Normalize(l.StoreCode))
            .Where(g => g.Count(l => !l.Archived) <= 1 && (g.Count() == 1 || g.Any(l => !l.Archived)))
            .ToDictionary(g => g.Key, g => (g.FirstOrDefault(l => !l.Archived) ?? g.First()).Id);

        var workplaces = await db.Workplaces.Where(w => w.StoreCode != null).ToListAsync(ct);
        var taken = workplaces.Where(w => w.LocationId != null).ToDictionary(w => w.LocationId!.Value, w => w.Id);
        var changed = 0;
        foreach (var w in workplaces)
        {
            if (!byStore.TryGetValue(Normalize(w.StoreCode!), out var locationId) || w.LocationId == locationId) continue;
            if (taken.TryGetValue(locationId, out var owner) && owner != w.Id) continue;

            if (w.LocationId is { } old) taken.Remove(old);
            w.LocationId = locationId;
            taken[locationId] = w.Id;
            changed++;
        }

        if (changed > 0) await db.SaveChangesAsync(ct);
        return changed;
    }

    /// <summary>Store-Nr. ohne führende Nullen und Leerzeichen, Großbuchstaben: <c>0012</c> und <c> 12</c> sind gleich.</summary>
    public static string Normalize(string storeCode) => storeCode.Trim().TrimStart('0').ToUpperInvariant();
}
