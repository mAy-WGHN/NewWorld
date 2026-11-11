using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Infrastructure.Persistence;
using Workforce.Core.Certificates;
using Workforce.Infrastructure.Absences;

namespace Workforce.Infrastructure.Certificates;

/// <summary>
/// Background work around the certificates: reminders to people who have not uploaded one, and deletion of files after their retention
/// period. Runs at an interval (not at start) and is safe to repeat: reminders are counted per sick note and the notifications are
/// idempotent. With several instances one round runs on one instance only (<see cref="IDistributedLock"/>); the others skip it.
/// Logs counts and the type of an error only, never content.
/// </summary>
public sealed partial class CertificateMaintenanceService(
    IServiceScopeFactory scopes, IDistributedLock locks, TimeProvider time, ILogger<CertificateMaintenanceService> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    internal const string LockName = "workforce.certificate-maintenance";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunOnceAsync(stoppingToken);
        }
    }

    /// <summary>One round: reminders, then retention. Errors are logged and do not stop the service.</summary>
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
            var service = scope.ServiceProvider.GetRequiredService<CertificateService>();
            var uow = scope.ServiceProvider.GetRequiredService<WorkforceUnitOfWork>();
            var reminders = await uow.RunAsync(() => service.SendRemindersAsync(cancellationToken), _ => true, cancellationToken);
            var purged = await uow.RunAsync(() => service.PurgeExpiredAsync(cancellationToken), _ => true, cancellationToken);
            var (rotated, remaining) = await service.RotateKeysAsync(cancellationToken);
            if (rotated > 0 || remaining > 0)
            {
                LogRotation(rotated, remaining);
            }

            LogRound(reminders, purged);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(ex.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Certificates: {Reminders} reminders created, {Purged} files deleted")]
    private partial void LogRound(int reminders, int purged);

    [LoggerMessage(Level = LogLevel.Information, Message = "Certificate key rotation: {Rotated} files encrypted anew, {Remaining} still under an older key (remove the older keys when this is 0)")]
    private partial void LogRotation(int rotated, int remaining);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Certificate maintenance: another instance is running it, round skipped")]
    private partial void LogSkipped();

    [LoggerMessage(Level = LogLevel.Error, Message = "Certificate maintenance failed ({ErrorType})")]
    private partial void LogFailed(string errorType);
}
