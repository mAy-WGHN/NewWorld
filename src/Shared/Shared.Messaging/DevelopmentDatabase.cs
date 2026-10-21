using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Shared.Messaging;

public static partial class DevelopmentDatabase
{
    /// <summary>
    /// Creates the service's schema (incl. outbox/inbox tables) in Development/Testing. A failure is logged, not thrown,
    /// so the service still starts and <c>/health/ready</c> reports the unreachable database.
    /// TODO(Migrationen): replace by EF migrations once the first entity exists; production never uses EnsureCreated.
    /// </summary>
    public static async Task EnsureCreatedAsync<TContext>(IServiceProvider services, IHostEnvironment environment)
        where TContext : DbContext
    {
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();
        try
        {
            await scope.ServiceProvider.GetRequiredService<TContext>().Database.EnsureCreatedAsync();
        }
#pragma warning disable CA1031 // see summary
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            LogFailed(scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DevelopmentDatabase)), ex.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Database initialization failed: {ExceptionType}")]
    private static partial void LogFailed(ILogger logger, string exceptionType);
}
