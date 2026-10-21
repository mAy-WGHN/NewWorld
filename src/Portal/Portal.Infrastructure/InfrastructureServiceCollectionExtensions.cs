using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shared.Authorization;
using Shared.Messaging;

namespace Portal.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>Connection string name of this service's database (provided by Aspire / user secrets, never in appsettings).</summary>
    public const string ConnectionName = "portal";

    public static IServiceCollection AddPortalInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var connectionString = configuration.GetConnectionString(ConnectionName)
            ?? throw new InvalidOperationException($"ConnectionStrings:{ConnectionName} is not configured.");

        services.AddDbContext<PortalDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", PortalDbContext.Schema))
            .UseSnakeCaseNamingConvention());
        services.AddHealthChecks().AddDbContextCheck<PortalDbContext>("database");

        services.AddMessaging<PortalDbContext>(configuration, environment);
        services.AddPermissionCachePersistence<PortalDbContext>();
        return services;
    }

    /// <summary>Creates the schema in Development/Testing (see <see cref="DevelopmentDatabase"/>).</summary>
    public static Task InitializePortalDatabaseAsync(this IServiceProvider services, IHostEnvironment environment) =>
        DevelopmentDatabase.EnsureCreatedAsync<PortalDbContext>(services, environment);
}
