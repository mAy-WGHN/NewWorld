using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shared.Authorization;
using Shared.Messaging;

namespace Workforce.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>Connection string name of this service's database (provided by Aspire / user secrets, never in appsettings).</summary>
    public const string ConnectionName = "workforce";

    public static IServiceCollection AddWorkforceInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var connectionString = configuration.GetConnectionString(ConnectionName)
            ?? throw new InvalidOperationException($"ConnectionStrings:{ConnectionName} is not configured.");

        services.AddDbContext<WorkforceDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", WorkforceDbContext.Schema))
            .UseSnakeCaseNamingConvention());
        services.AddHealthChecks().AddDbContextCheck<WorkforceDbContext>("database");

        services.AddMessaging<WorkforceDbContext>(configuration, environment);
        services.AddPermissionCachePersistence<WorkforceDbContext>();
        return services;
    }

    /// <summary>Creates the schema in Development/Testing (see <see cref="DevelopmentDatabase"/>).</summary>
    public static Task InitializeWorkforceDatabaseAsync(this IServiceProvider services, IHostEnvironment environment) =>
        DevelopmentDatabase.EnsureCreatedAsync<WorkforceDbContext>(services, environment);
}
