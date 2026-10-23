using Access.Contracts;
using BcIntegration.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Notifications.Contracts;
using Notifications.Core;
using Notifications.Core.Delivery;
using Notifications.Core.Templates;
using Notifications.Infrastructure.Delivery;
using Notifications.Infrastructure.Messaging;
using Notifications.Infrastructure.Templates;
using Shared.Infrastructure.Authorization;
using Shared.Infrastructure.Messaging;
using Shared.Infrastructure.Persistence;
using Shared.Messaging;

namespace Notifications.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>Connection string name of this service's database (provided by Aspire / user secrets, never in appsettings).</summary>
    public const string ConnectionName = "notifications";

    public static IServiceCollection AddNotificationsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var connectionString = configuration.GetConnectionString(ConnectionName)
            ?? throw new InvalidOperationException($"ConnectionStrings:{ConnectionName} is not configured.");

        services.AddDbContext<NotificationsDbContext>(options => NotificationsDbContext.Configure(options, connectionString));
        services.AddHealthChecks().AddDbContextCheck<NotificationsDbContext>("database");

        services.AddMessaging<NotificationsDbContext>(configuration, environment);
        services.AddPermissionCachePersistence<NotificationsDbContext>();

        // Permission copy from Access (decision 13). Subscription "notifications" on the PermissionsChanged topic.
        // Register the privileges of this API with Access (only if Access is configured; tests and local runs without Access skip it).
        if (configuration["Access:BaseUrl"] is { Length: > 0 })
        {
            services.AddCatalogRegistration(configuration, environment, NotificationPrivileges.AppKey, [.. NotificationPrivileges.All.Select(k => new CatalogPrivilege(k, false))]);
        }

        var options = configuration.GetSection(NotificationOptions.ConfigurationSection).Get<NotificationOptions>() ?? new NotificationOptions();
        services.AddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(TemplateLoader.Load(options.TemplatesPath ?? Path.Combine(AppContext.BaseDirectory, "Templates")));
        services.AddSingleton<SentMessages>();
        services.AddScoped<IPushSender, FakePushSender>();
        services.AddScoped<IEmailSender, FakeEmailSender>();
        services.AddScoped<INotificationStore, EfNotificationStore>();
        services.AddScoped<EfPreferenceStore>();
        services.AddScoped<IPreferenceStore>(sp => sp.GetRequiredService<EfPreferenceStore>());
        services.AddScoped<IPreferenceWriter>(sp => sp.GetRequiredService<EfPreferenceStore>());
        services.AddScoped<NotificationDispatcher>();
        services.AddScoped<PreferenceService>();
        services.AddScoped<NotificationCleaner>();
        services.AddHostedService<NotificationCleanupService>();

        // Commands from all business APIs: queue readable only by Notifications. Erasure: subscription "notifications".
        services.AddMessageHandler<NotificationsDbContext, SendNotification, SendNotificationHandler>(MessageSource.Queue(MessagingNames.For(typeof(SendNotification))));
        services.AddMessageHandler<NotificationsDbContext, EmployeeDataErasureRequested, EmployeeDataErasureRequestedHandler>(
            MessageSource.Subscription(MessagingNames.For(typeof(EmployeeDataErasureRequested)), "notifications"));
        services.AddMessageHandler<NotificationsDbContext, PermissionsChanged, PermissionsChangedConsumer>(
            MessageSource.Subscription(MessagingNames.For(typeof(PermissionsChanged)), "notifications"));
        return services;
    }

    /// <summary>Applies the migrations in Development/Testing (see <see cref="DatabaseInitializer"/>).</summary>
    public static Task MigrateNotificationsDatabaseAsync(this IServiceProvider services, IHostEnvironment environment) =>
        DatabaseInitializer.MigrateInDevelopmentAsync<NotificationsDbContext>(services, environment);
}
