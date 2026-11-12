using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Infrastructure.Persistence;

namespace Portal.Infrastructure.Logbook;

/// <summary>
/// Once an hour: deletes the reports of the Berichtsheft that are past their retention (<see cref="LogbookRetentionService"/>) and sends the automatic reminders of the Berichtsheft (decision 49): the store managers of a store without a handed-in report for the week get a
/// reminder at the points of the settings. With several instances one round runs on one instance only (<see cref="IDistributedLock"/>); a reminder is
/// unique per store, week and point, so a repeated round sends nothing twice. Logs counts and the type of an error only, never content.
/// </summary>
public sealed partial class LogbookReminderBackgroundService(
    IServiceScopeFactory scopes, IDistributedLock locks, TimeProvider time, ILogger<LogbookReminderBackgroundService> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    internal const string LockName = "portal.logbook-reminders";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunOnceAsync(stoppingToken);
        }
    }

    /// <summary>One round. Errors are logged and do not stop the service.</summary>
    public async Task RunOnceAsync(CancellationToken cancellationToken)
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
            var sent = await scope.ServiceProvider.GetRequiredService<LogbookReminderService>().RunAutomaticAsync(cancellationToken);
            LogRound(sent);
            var deleted = await scope.ServiceProvider.GetRequiredService<LogbookRetentionService>().RunAsync(cancellationToken);
            if (deleted > 0)
            {
                LogRetention(deleted);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(ex.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Logbook reminders: {Count} sent")]
    private partial void LogRound(int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Logbook retention: {Count} reports deleted")]
    private partial void LogRetention(int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Logbook reminders: another instance is running it, round skipped")]
    private partial void LogSkipped();

    [LoggerMessage(Level = LogLevel.Error, Message = "Logbook reminders failed ({ErrorType})")]
    private partial void LogFailed(string errorType);
}
