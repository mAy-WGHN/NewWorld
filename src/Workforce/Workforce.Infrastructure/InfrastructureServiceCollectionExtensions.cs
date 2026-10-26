using Access.Contracts;
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
using Workforce.Core.Absences;
using Workforce.Infrastructure.Absences;
using Workforce.Infrastructure.Messaging;
using Workforce.Infrastructure.People;

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

        services.AddDbContext<WorkforceDbContext>(options => WorkforceDbContext.Configure(options, connectionString));
        services.AddHealthChecks().AddDbContextCheck<WorkforceDbContext>("database");

        services.AddMessaging<WorkforceDbContext>(configuration, environment);
        services.AddPermissionCachePersistence<WorkforceDbContext>();

        // Permission copy from Access (decision 13). Subscription "workforce" on the PermissionsChanged topic.
        var options = configuration.GetSection(WorkforceOptions.ConfigurationSection).Get<WorkforceOptions>() ?? new WorkforceOptions();
        services.AddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IHolidayCalendar, GermanHolidayCalendar>();
        services.AddScoped<IAbsenceStore, EfAbsenceStore>();
        services.AddScoped<IPeopleDirectory, EfPeopleDirectory>();
        services.AddScoped<IEntitlementStore, EfEntitlementStore>();
        services.AddScoped<IResponsibilityDirectory, EfResponsibilityDirectory>();
        services.AddScoped<IWorkforceMessages, OutboxWorkforceMessages>();
        services.AddScoped<AbsenceService>();
        services.AddScoped<WorkforceUnitOfWork>();

        // Register the privileges of this API with Access (only if Access is configured).
        if (configuration["Access:BaseUrl"] is { Length: > 0 })
        {
            services.AddCatalogRegistration(configuration, AbsencePrivileges.AppKey, [.. AbsencePrivileges.Catalog.Select(p => new CatalogPrivilege(p.Key, p.Scopable))]);
        }

        // Copies and results: subscriptions "workforce" on the topics (purpose: employee/store copies, approvers, BC sync status, erasure).
        services.AddMessageHandler<WorkforceDbContext, EmployeeChanged, EmployeeChangedHandler>(Subscription<EmployeeChanged>());
        services.AddMessageHandler<WorkforceDbContext, EmployeeLeft, EmployeeLeftHandler>(Subscription<EmployeeLeft>());
        services.AddMessageHandler<WorkforceDbContext, StoreChanged, StoreChangedHandler>(Subscription<StoreChanged>());
        services.AddMessageHandler<WorkforceDbContext, EmployeeDataErasureRequested, EmployeeDataErasureRequestedHandler>(Subscription<EmployeeDataErasureRequested>());
        services.AddMessageHandler<WorkforceDbContext, ResponsibilitiesChanged, ResponsibilitiesChangedHandler>(Subscription<ResponsibilitiesChanged>());
        services.AddMessageHandler<WorkforceDbContext, BcSyncSucceeded, BcSyncSucceededHandler>(Subscription<BcSyncSucceeded>());
        services.AddMessageHandler<WorkforceDbContext, BcSyncFailed, BcSyncFailedHandler>(Subscription<BcSyncFailed>());
        services.AddMessageHandler<WorkforceDbContext, PermissionsChanged, PermissionsChangedConsumer>(
            MessageSource.Subscription(MessagingNames.For(typeof(PermissionsChanged)), "workforce"));
        return services;
    }

    private static MessageSource Subscription<TMessage>() => MessageSource.Subscription(MessagingNames.For(typeof(TMessage)), "workforce");

    /// <summary>Applies the migrations in Development/Testing (see <see cref="DatabaseInitializer"/>).</summary>
    public static Task MigrateWorkforceDatabaseAsync(this IServiceProvider services, IHostEnvironment environment) =>
        DatabaseInitializer.MigrateInDevelopmentAsync<WorkforceDbContext>(services, environment);
}
