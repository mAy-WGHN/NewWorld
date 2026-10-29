using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Workforce.Core.WorkingTime;
using Workforce.Infrastructure.Absences;

namespace Workforce.Infrastructure.WorkingTime;

/// <summary>
/// Background work around the working time account: the reminder to HR, the day after somebody has left, that a balance is still to be paid out.
/// Runs at an interval (not at start) and is safe to repeat: every person is looked at once and the notification is idempotent.
/// TODO(Betrieb): with several instances both would run; a lock is needed then (like the certificate maintenance). Logs counts and the type of an
/// error only, never content.
/// </summary>
public sealed partial class WorkingTimeMaintenanceService(
    IServiceScopeFactory scopes, TimeProvider time, ILogger<WorkingTimeMaintenanceService> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

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
            await using var scope = scopes.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<WorkingTimeService>();
            var uow = scope.ServiceProvider.GetRequiredService<WorkforceUnitOfWork>();
            var reminders = await uow.RunAsync(() => service.RemindPayoutAfterExitAsync(cancellationToken), _ => true, cancellationToken);
            LogRound(reminders);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(ex.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Working time: {Reminders} payout reminders created")]
    private partial void LogRound(int reminders);

    [LoggerMessage(Level = LogLevel.Error, Message = "Working time maintenance failed ({ErrorType})")]
    private partial void LogFailed(string errorType);
}
