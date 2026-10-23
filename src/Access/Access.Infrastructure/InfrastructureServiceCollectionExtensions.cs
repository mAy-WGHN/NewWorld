using Access.Core;
using Access.Core.Messaging;
using Access.Core.Publishing;
using Access.Infrastructure.Acl;
using Access.Infrastructure.Messaging;
using Access.Infrastructure.People;
using Access.Infrastructure.Publishing;
using BcIntegration.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Shared.Infrastructure.Authorization;
using Shared.Infrastructure.Messaging;
using Shared.Infrastructure.Persistence;
using Shared.Messaging;

namespace Access.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>Connection string name of this service's database (provided by Aspire / user secrets, never in appsettings).</summary>
    public const string ConnectionName = "access";

    public static IServiceCollection AddAccessInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var connectionString = configuration.GetConnectionString(ConnectionName)
            ?? throw new InvalidOperationException($"ConnectionStrings:{ConnectionName} is not configured.");

        services.AddDbContext<AccessDbContext>(options => AccessDbContext.Configure(options, connectionString));
        services.AddHealthChecks().AddDbContextCheck<AccessDbContext>("database");

        services.AddMessaging<AccessDbContext>(configuration, environment);
        services.AddPermissionCachePersistence<AccessDbContext>();

        var options = configuration.GetSection(AccessOptions.ConfigurationSection).Get<AccessOptions>() ?? new AccessOptions();
        services.AddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IEventPublisher, OutboxEventPublisher>();
        services.AddScoped<IAclReader, EfAclReader>();
        services.AddScoped<IPublicationStore, EfPublicationStore>();
        services.AddScoped<Recalculator>();
        services.AddScoped<RecalculationRunner>();
        services.AddScoped<AclAdministration>();
        services.AddHostedService<AccessBootstrap>();
        services.AddHostedService<RecalculationService>();

        // Copies from BC Integration: one subscription "access" per topic (purpose: permissions and responsibilities).
        services.AddMessageHandler<AccessDbContext, EmployeeChanged, EmployeeChangedHandler>(Subscription<EmployeeChanged>());
        services.AddMessageHandler<AccessDbContext, EmployeeHired, EmployeeHiredHandler>(Subscription<EmployeeHired>());
        services.AddMessageHandler<AccessDbContext, EmployeeLeft, EmployeeLeftHandler>(Subscription<EmployeeLeft>());
        services.AddMessageHandler<AccessDbContext, StoreChanged, StoreChangedHandler>(Subscription<StoreChanged>());
        services.AddMessageHandler<AccessDbContext, EmployeeDataErasureRequested, EmployeeDataErasureRequestedHandler>(Subscription<EmployeeDataErasureRequested>());
        return services;
    }

    public const string SubscriptionName = "access";

    private static MessageSource Subscription<TMessage>() => MessageSource.Subscription(MessagingNames.For(typeof(TMessage)), SubscriptionName);

    /// <summary>Applies the migrations in Development/Testing (see <see cref="DatabaseInitializer"/>).</summary>
    public static Task MigrateAccessDatabaseAsync(this IServiceProvider services, IHostEnvironment environment) =>
        DatabaseInitializer.MigrateInDevelopmentAsync<AccessDbContext>(services, environment);
}
