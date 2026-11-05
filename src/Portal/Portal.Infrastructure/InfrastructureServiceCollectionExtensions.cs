using Access.Contracts;
using BcIntegration.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Portal.Core;
using Portal.Core.Faqs;
using Portal.Infrastructure.Faqs;
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

        // FAQ images: files outside the application folder (never in the web root); relative paths count from the content root.
        var imagePath = configuration["Portal:Faq:ImagePath"] is { Length: > 0 } configured ? configured : Path.Combine("var", "uploads", "faq");
        services.AddSingleton<IFaqImageStorage>(_ => new FileSystemFaqImageStorage(Path.GetFullPath(imagePath, environment.ContentRootPath)));
        var attachmentPath = configuration["Portal:Faq:AttachmentPath"] is { Length: > 0 } attachments ? attachments : Path.Combine("var", "uploads", "faq-files");
        services.AddSingleton<IFaqAttachmentStorage>(_ => new FileSystemFaqAttachmentStorage(Path.GetFullPath(attachmentPath, environment.ContentRootPath)));

        // Register the privileges of this API with Access (only if Access is configured).
        if (configuration["Access:BaseUrl"] is { Length: > 0 })
        {
            services.AddCatalogRegistration(
                configuration, PortalPrivileges.AppKey,
                [.. PortalPrivileges.Catalog.Select(p => new CatalogPrivilege(p.Key, p.Scopable, p.Description))]);
        }

        // Permission copy from Access (decision 13). Subscription "portal" on the PermissionsChanged topic.
        services.AddMessageHandler<PortalDbContext, PermissionsChanged, PermissionsChangedConsumer>(
            MessageSource.Subscription(MessagingNames.For(typeof(PermissionsChanged)), "portal"));

        // Copies of employees and stores (read-only) and erasure: subscriptions "portal". The Portal API has no business functions yet;
        // these copies prove the event distribution to a third consumer.
        services.AddMessageHandler<PortalDbContext, EmployeeChanged, EmployeeChangedHandler>(
            MessageSource.Subscription(MessagingNames.For(typeof(EmployeeChanged)), "portal"));
        services.AddMessageHandler<PortalDbContext, EmployeeHired, EmployeeHiredHandler>(
            MessageSource.Subscription(MessagingNames.For(typeof(EmployeeHired)), "portal"));
        services.AddMessageHandler<PortalDbContext, EmployeeLeft, EmployeeLeftHandler>(
            MessageSource.Subscription(MessagingNames.For(typeof(EmployeeLeft)), "portal"));
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
