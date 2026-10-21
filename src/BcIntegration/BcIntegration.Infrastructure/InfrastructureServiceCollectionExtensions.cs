using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shared.Authorization;
using Shared.Messaging;

namespace BcIntegration.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>Connection string name of this service's database (provided by Aspire / user secrets, never in appsettings).</summary>
    public const string ConnectionName = "bcintegration";

    public static IServiceCollection AddBcIntegrationInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var connectionString = configuration.GetConnectionString(ConnectionName)
            ?? throw new InvalidOperationException($"ConnectionStrings:{ConnectionName} is not configured.");

        services.AddDbContext<BcIntegrationDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", BcIntegrationDbContext.Schema))
            .UseSnakeCaseNamingConvention());
        services.AddHealthChecks().AddDbContextCheck<BcIntegrationDbContext>("database");

        services.AddMessaging<BcIntegrationDbContext>(configuration, environment);
        services.AddPermissionCachePersistence<BcIntegrationDbContext>();
        return services;
    }

    /// <summary>Creates the schema in Development/Testing (see <see cref="DevelopmentDatabase"/>).</summary>
    public static Task InitializeBcIntegrationDatabaseAsync(this IServiceProvider services, IHostEnvironment environment) =>
        DevelopmentDatabase.EnsureCreatedAsync<BcIntegrationDbContext>(services, environment);
}
