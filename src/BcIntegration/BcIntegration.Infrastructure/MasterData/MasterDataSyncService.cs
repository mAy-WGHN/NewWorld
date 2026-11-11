using BcIntegration.Core.Errors;
using BcIntegration.Core.MasterData;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Infrastructure.Persistence;

namespace BcIntegration.Infrastructure.MasterData;

/// <summary>
/// Compares master data with BC at the configured interval. A BC webhook can later call the same
/// <see cref="MasterDataSynchronizer"/>. With several replicas one round runs on one instance only (<see cref="IDistributedLock"/>).
/// </summary>
public sealed partial class MasterDataSyncService(
    IServiceScopeFactory scopes,
    IDistributedLock locks,
    TimeProvider time,
    IOptions<MasterDataSyncOptions> options,
    ILogger<MasterDataSyncService> logger) : BackgroundService
{
    internal const string LockName = "bcintegration.master-data-sync";

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
            await using var held = await locks.TryAcquireAsync(LockName, cancellationToken);
            if (held is null)
            {
                LogSkipped();
                return;
            }

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

    [LoggerMessage(Level = LogLevel.Debug, Message = "Master data sync: another instance is running it, round skipped")]
    private partial void LogSkipped();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Master data sync failed ({Kind}): {Reason}")]
    private partial void LogFailed(string kind, string reason);
}
