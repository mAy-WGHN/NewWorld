using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Shared.Infrastructure.Persistence;

public static partial class DatabaseInitializer
{
    /// <summary>The argument that turns a start into the deployment step: <c>dotnet Workforce.Api.dll --migrate</c>.</summary>
    public const string MigrateOnlyArgument = "--migrate";

    /// <summary>
    /// With <see cref="MigrateOnlyArgument"/>: applies the migrations in any environment and returns <c>true</c>, so that the program ends
    /// before anything runs (no background services, no listener). A failure is not caught: the process ends with an error and the deployment
    /// fails instead of starting an application against a database in an unknown state. Without it: Development/Testing migrate at start
    /// (<see cref="MigrateInDevelopmentAsync{TContext}"/>) and the result is <c>false</c>; production never migrates at start.
    /// </summary>
    public static async Task<bool> MigrateOrExitAsync<TContext>(IServiceProvider services, IHostEnvironment environment, string[] args)
        where TContext : DbContext
    {
        if (!args.Contains(MigrateOnlyArgument, StringComparer.Ordinal))
        {
            await MigrateInDevelopmentAsync<TContext>(services, environment);
            return false;
        }

        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TContext>().Database.MigrateAsync();
        return true;
    }

    /// <summary>
    /// Applies the service's EF migrations in Development/Testing. A failure is logged (exception type only), not thrown,
    /// so the service still starts and <c>/health/ready</c> reports the unreachable database. Production applies migrations
    /// as a separate deployment step (<see cref="MigrateOrExitAsync{TContext}"/>), never at startup.
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
