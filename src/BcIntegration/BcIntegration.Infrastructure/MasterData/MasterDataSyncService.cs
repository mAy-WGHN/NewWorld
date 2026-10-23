using BcIntegration.Core.Errors;
using BcIntegration.Core.MasterData;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BcIntegration.Infrastructure.MasterData;

/// <summary>
/// Compares master data with BC at the configured interval. A BC webhook can later call the same
/// <see cref="MasterDataSynchronizer"/>. TODO(Betrieb): one instance only; several replicas need a lock.
/// </summary>
public sealed partial class MasterDataSyncService(
    IServiceScopeFactory scopes,
    TimeProvider time,
    IOptions<MasterDataSyncOptions> options,
    ILogger<MasterDataSyncService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.Interval, time);
        do
        {
            await RunOnceAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<MasterDataSynchronizer>().SyncAsync(cancellationToken);
            LogCompleted(result.StoresChanged, result.EmployeesChanged, result.Hired, result.Left);
        }
        catch (BcTransientFailureException ex)
        {
            LogFailed("transient", ex.Reason);
        }
        catch (BcTechnicalFailureException ex)
        {
            LogFailed("technical", ex.Reason);
        }
#pragma warning disable CA1031 // the poller must survive; the next run starts from the last watermark
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            LogFailed("unexpected", ex.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Master data sync: {Stores} stores, {Employees} employees changed, {Hired} hired, {Left} left")]
    private partial void LogCompleted(int stores, int employees, int hired, int left);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Master data sync failed ({Kind}): {Reason}")]
    private partial void LogFailed(string kind, string reason);
}
