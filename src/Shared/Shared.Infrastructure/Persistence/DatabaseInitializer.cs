using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Shared.Infrastructure.Persistence;

public static partial class DatabaseInitializer
{
    /// <summary>
    /// Applies the service's EF migrations in Development/Testing. A failure is logged (exception type only), not thrown,
    /// so the service still starts and <c>/health/ready</c> reports the unreachable database.
    /// TODO(Deployment): production applies migrations as a separate deployment step, never at startup.
    /// </summary>
    public static async Task MigrateInDevelopmentAsync<TContext>(IServiceProvider services, IHostEnvironment environment)
        where TContext : DbContext
    {
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();
        try
        {
            await scope.ServiceProvider.GetRequiredService<TContext>().Database.MigrateAsync();
        }
#pragma warning disable CA1031 // see summary
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            LogFailed(scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseInitializer)), ex.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Database migration failed: {ExceptionType}")]
    private static partial void LogFailed(ILogger logger, string exceptionType);
}
