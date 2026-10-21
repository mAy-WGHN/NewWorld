using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Shared.Infrastructure.Messaging;

/// <summary>Polls the outbox and sends pending messages; also removes expired entries once per cleanup interval.</summary>
public sealed partial class OutboxDispatcherService<TContext>(
    IServiceScopeFactory scopes,
    TimeProvider time,
    MessagingOptions options,
    ILogger<OutboxDispatcherService<TContext>> logger) : BackgroundService where TContext : DbContext
{
    private DateTimeOffset _nextCleanup = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
#pragma warning disable CA1031 // the loop must survive any failure of one iteration
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
            {
                LogIterationFailed(ex.GetType().Name);
            }

            await Task.Delay(options.PollInterval, time, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    internal async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<OutboxDispatcher<TContext>>().DispatchPendingAsync(cancellationToken);

        if (time.GetUtcNow() >= _nextCleanup)
        {
            await scope.ServiceProvider.GetRequiredService<MessagingCleaner<TContext>>().CleanAsync(cancellationToken);
            _nextCleanup = time.GetUtcNow() + options.CleanupInterval;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox iteration failed: {ExceptionType}")]
    private partial void LogIterationFailed(string exceptionType);
}
