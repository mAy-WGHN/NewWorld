using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Notifications.Infrastructure.Delivery;

public sealed partial class NotificationCleanupService(
    IServiceScopeFactory scopes, TimeProvider time, NotificationOptions options, ILogger<NotificationCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.CleanupInterval, time);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var removed = await scope.ServiceProvider.GetRequiredService<NotificationCleaner>().CleanAsync(stoppingToken);
                if (removed > 0)
                {
                    LogRemoved(removed);
                }
            }
#pragma warning disable CA1031 // the next run starts from the same state
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
            {
                LogFailed(ex.GetType().Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Retention removed {Count} rows")]
    private partial void LogRemoved(int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Retention run failed: {ExceptionType}")]
    private partial void LogFailed(string exceptionType);
}
