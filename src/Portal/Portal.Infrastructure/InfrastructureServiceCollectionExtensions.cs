using Access.Contracts;
using BcIntegration.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Portal.Infrastructure.Messaging;
using Shared.Infrastructure.Authorization;
using Shared.Infrastructure.Messaging;
using Shared.Infrastructure.Persistence;
using Shared.Messaging;
using Workforce.Contracts;

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

        services.AddDbContext<PortalDbContext>(options => PortalDbContext.Configure(options, connectionString));
        services.AddHealthChecks().AddDbContextCheck<PortalDbContext>("database");

        services.AddMessaging<PortalDbContext>(configuration, environment);
        services.AddPermissionCachePersistence<PortalDbContext>();

        // Permission copy from Access (decision 13). Subscription "portal" on the PermissionsChanged topic.
        services.AddMessageHandler<PortalDbContext, PermissionsChanged, PermissionsChangedConsumer>(
            MessageSource.Subscription(MessagingNames.For(typeof(PermissionsChanged)), "portal"));

        // Copies of employees and stores (read-only) and erasure: subscriptions "portal". The Portal API has no business functions yet;
        // these copies prove the event distribution to a third consumer.
        services.AddMessageHandler<PortalDbContext, EmployeeChanged, EmployeeChangedHandler>(
            MessageSource.Subscription(MessagingNames.For(typeof(EmployeeChanged)), "portal"));
        services.AddMessageHandler<PortalDbContext, StoreChanged, StoreChangedHandler>(
            MessageSource.Subscription(MessagingNames.For(typeof(StoreChanged)), "portal"));
        services.AddMessageHandler<PortalDbContext, EmployeeDataErasureRequested, EmployeeDataErasureRequestedHandler>(
            MessageSource.Subscription(MessagingNames.For(typeof(EmployeeDataErasureRequested)), "portal"));
        services.AddMessageHandler<PortalDbContext, EmployeeAvailabilityChanged, EmployeeAvailabilityChangedHandler>(
            MessageSource.Subscription(MessagingNames.For(typeof(EmployeeAvailabilityChanged)), "portal"));
        return services;
    }

    /// <summary>Applies the migrations in Development/Testing (see <see cref="DatabaseInitializer"/>).</summary>
    public static Task MigratePortalDatabaseAsync(this IServiceProvider services, IHostEnvironment environment) =>
        DatabaseInitializer.MigrateInDevelopmentAsync<PortalDbContext>(services, environment);
}
