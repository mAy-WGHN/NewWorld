using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Portal.Core.Logbook;

namespace Portal.Infrastructure.Logbook;

/// <summary>
/// Local development only (the AppHost sets <see cref="LogbookDemoSeedOptions"/>; no other environment does): sets up the hierarchy of the Berichtsheft
/// for a demo, so a regional and a district leader see the reports without anybody maintaining the hierarchy by hand: one district led by the district
/// leader, one region led by the regional leader, and the given stores in that region. The people and stores arrive as copies a little after the start,
/// so the service tries every few seconds until they are there. It never overwrites a hierarchy that exists: as soon as somebody has maintained one,
/// it does nothing. Logs only whether it ran, never names.
/// </summary>
public sealed partial class LogbookDemoSeedService(
    IServiceScopeFactory scopes, LogbookDemoSeedOptions options, TimeProvider time, ILogger<LogbookDemoSeedService> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    /// <summary>Fixed ids so the demo hierarchy looks the same on every start.</summary>
    public static readonly Guid DistrictId = new("0d0d0d0d-0000-4000-8000-000000000001");

    public static readonly Guid RegionId = new("0d0d0d0d-0000-4000-8000-000000000002");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.IsActive)
        {
            return;
        }

        using var timer = new PeriodicTimer(Interval, time);
        do
        {
            if (await TryApplyAsync(stoppingToken))
            {
                return;
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>One try; true when there is nothing more to do (done now or already maintained), false when people or stores are not there yet.</summary>
    public async Task<bool> TryApplyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var store = scope.ServiceProvider.GetRequiredService<LogbookStructureStore>();

            var current = await store.LoadAsync(cancellationToken);
            if (current.Districts.Count > 0 || current.Regions.Count > 0 || current.Stores.Count > 0)
            {
                return true; // somebody maintains the hierarchy: leave it alone
            }

            var numbers = options.StoreNumbers.Distinct().ToList();
            var stores = await db.Stores.AsNoTracking().Where(s => numbers.Contains(s.StoreNumber)).Select(s => s.StoreId).ToListAsync(cancellationToken);
            var regional = await PersonAsync(db, options.RegionalLeader, cancellationToken);
            var district = await PersonAsync(db, options.DistrictLeader, cancellationToken);
            if (numbers.Count == 0 || stores.Count != numbers.Count || regional is null || district is null)
            {
                return false;
            }

            await store.SaveAsync(
                new LogbookStructure(
                    [new LogbookDistrict(DistrictId, options.DistrictName, [district.Value])],
                    [new LogbookRegion(RegionId, DistrictId, options.RegionName, [regional.Value])],
                    [.. stores.Select(s => new LogbookStoreAssignment(s, DistrictId, RegionId))]),
                cancellationToken);
            LogApplied(stores.Count);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(ex.GetType().Name);
            return false;
        }
    }

    private static async Task<Guid?> PersonAsync(PortalDbContext db, string personnelNumber, CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(personnelNumber)
            ? null
            : await db.People.AsNoTracking().Where(p => p.PersonnelNumber == personnelNumber).Select(p => (Guid?)p.EmployeeId).FirstOrDefaultAsync(cancellationToken);

    [LoggerMessage(Level = LogLevel.Information, Message = "Logbook demo hierarchy set up with {Count} stores")]
    private partial void LogApplied(int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Logbook demo hierarchy failed: {ErrorType}")]
    private partial void LogFailed(string errorType);
}

/// <summary>Settings of the local demo hierarchy (section <c>Portal:DevelopmentSeed:Logbook</c>, set only by the AppHost). Empty = no seed.</summary>
public sealed class LogbookDemoSeedOptions
{
    public const string ConfigurationSection = "Portal:DevelopmentSeed:Logbook";

    public string DistrictName { get; set; } = "Demo-Bezirk";

    public string RegionName { get; set; } = "Demo-Region";

    /// <summary>Store numbers (as in BC) that belong to the demo region.</summary>
    public List<string> StoreNumbers { get; set; } = [];

    public string RegionalLeader { get; set; } = string.Empty;

    public string DistrictLeader { get; set; } = string.Empty;

    public bool IsActive => StoreNumbers.Count > 0 && RegionalLeader.Length > 0 && DistrictLeader.Length > 0;
}
