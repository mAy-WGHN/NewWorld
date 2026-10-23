using BcIntegration.Core.MasterData;
using BcIntegration.Core.Messaging;
using BcIntegration.Core.Workforce;
using BcIntegration.Infrastructure.BusinessCentral;
using BcIntegration.Infrastructure.MasterData;
using BcIntegration.Infrastructure.Messaging;
using BcIntegration.Infrastructure.Workforce;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shared.Authorization;
using Shared.Infrastructure.Authorization;
using Shared.Infrastructure.Messaging;
using Shared.Infrastructure.Persistence;
using Shared.Messaging;
using Workforce.Contracts;

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

        services.AddDbContext<BcIntegrationDbContext>(options => BcIntegrationDbContext.Configure(options, connectionString));
        services.AddHealthChecks().AddDbContextCheck<BcIntegrationDbContext>("database");

        services.AddMessaging<BcIntegrationDbContext>(configuration, environment);
        services.AddPermissionCachePersistence<BcIntegrationDbContext>();

        // Ports of the core.
        services.AddScoped<IEventPublisher, OutboxEventPublisher>();
        services.AddScoped<IMasterDataStore, EfMasterDataStore>();
        services.AddScoped<IAbsenceSyncStore, EfAbsenceSyncStore>();
        if (configuration[$"{BcClientOptions.ConfigurationSection}:Credential"] == TokenCredentialFactory.LocalFake
            && !environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        {
            throw new InvalidOperationException("The local fake credential for BC is only allowed in Development/Testing.");
        }

        services.AddBcClient(configuration);
        services.AddScoped<IBcMasterDataSource>(sp => sp.GetRequiredService<BcClient>());
        services.AddScoped<IBcAbsenceWriter, BcAbsenceWriter>();

        // Master data (MasterData/).
        services.Configure<MasterDataSyncOptions>(configuration.GetSection(MasterDataSyncOptions.ConfigurationSection));
        services.AddScoped(sp => new MasterDataSynchronizer(
            sp.GetRequiredService<IBcMasterDataSource>(), sp.GetRequiredService<IMasterDataStore>(), sp.GetRequiredService<IEventPublisher>(),
            sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<IOptions<MasterDataSyncOptions>>().Value));
        services.AddScoped<EmployeeErasure>();
        if (configuration[$"{BcClientOptions.ConfigurationSection}:BaseUrl"] is { Length: > 0 })
        {
            services.AddHostedService<MasterDataSyncService>();
        }

        // Absences (Workforce/): queue readable only by BC Integration.
        services.AddScoped<AbsenceSynchronizer>();
        services.AddMessageHandler<BcIntegrationDbContext, SyncAbsenceToBc, SyncAbsenceToBcHandler>(
            MessageSource.Queue(MessagingNames.For(typeof(SyncAbsenceToBc))));
        return services;
    }

    /// <summary>Applies the migrations in Development/Testing (see <see cref="DatabaseInitializer"/>).</summary>
    public static Task MigrateBcIntegrationDatabaseAsync(this IServiceProvider services, IHostEnvironment environment) =>
        DatabaseInitializer.MigrateInDevelopmentAsync<BcIntegrationDbContext>(services, environment);
}
