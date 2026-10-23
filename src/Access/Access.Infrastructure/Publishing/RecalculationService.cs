using Access.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Access.Infrastructure.Publishing;

/// <summary>Periodic recalculation, so time-limited assignments and exit dates take effect on time.</summary>
public sealed partial class RecalculationService(
    IServiceScopeFactory scopes,
    TimeProvider time,
    AccessOptions options,
    ILogger<RecalculationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.RecalculationInterval, time);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<RecalculationRunner>().RunAsync(null, stoppingToken);
                if (result.Permissions.Count + result.Responsibilities.Count > 0)
                {
                    LogPublished(result.Permissions.Count, result.Responsibilities.Count);
                }
            }
#pragma warning disable CA1031 // the timer must survive; the next run recalculates everything again
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
            {
                LogFailed(ex.GetType().Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Published {Permissions} permission and {Responsibilities} responsibility changes")]
    private partial void LogPublished(int permissions, int responsibilities);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recalculation failed: {ExceptionType}")]
    private partial void LogFailed(string exceptionType);
}
