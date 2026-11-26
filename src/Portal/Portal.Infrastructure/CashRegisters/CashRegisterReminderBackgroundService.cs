using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Infrastructure.Persistence;

namespace Portal.Infrastructure.CashRegisters;

/// <summary>
/// Every quarter of an hour: sends the daily reminder for cash registers that are not sealed (<see cref="CashRegisterReminderService"/>). With several
/// instances one round runs on one instance only (<see cref="IDistributedLock"/>); a day is reminded once. Logs counts and the type of an error only.
/// </summary>
public sealed partial class CashRegisterReminderBackgroundService(IServiceScopeFactory scopes, IDistributedLock locks, TimeProvider time, ILogger<CashRegisterReminderBackgroundService> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);
    internal const string LockName = "portal.cash-register-reminders";

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
                return;
            }

            await using var scope = scopes.CreateAsyncScope();
            var sent = await scope.ServiceProvider.GetRequiredService<CashRegisterReminderService>().RunAutomaticAsync(cancellationToken);
            if (sent > 0)
            {
                LogRound(sent);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(ex.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Cash register reminders: {Count} notifications sent")]
    private partial void LogRound(int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Cash register reminders failed ({ErrorType})")]
    private partial void LogFailed(string errorType);
}
