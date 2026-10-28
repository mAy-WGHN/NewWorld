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
using Workforce.Core.Certificates;
using Workforce.Infrastructure.Certificates;
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
        services.AddScoped<Workforce.Core.Employees.IEmployeeCardStore, Employees.EfEmployeeCardStore>();
        services.AddScoped<Workforce.Core.Employees.IReferenceLists, Employees.EfReferenceLists>();
        services.AddScoped<Workforce.Core.Employees.IPersonnelNumberSeries, Employees.EfPersonnelNumberSeries>();
        services.AddScoped<Workforce.Core.Employees.EmployeeService>();
        services.AddScoped<Workforce.Core.Employees.IEmployeeChangeStore, Employees.EfEmployeeChangeStore>();
        services.AddScoped<Workforce.Core.Employees.EmployeeChangeService>();
        services.AddScoped<IExtraVacationStore, EfExtraVacationStore>();
        services.AddScoped<IResponsibilityDirectory, EfResponsibilityDirectory>();
        services.AddScoped<IWorkforceMessages, OutboxWorkforceMessages>();
        services.AddScoped<Workforce.Core.Unavailability.IUnavailabilityStore, Unavailability.EfUnavailabilityStore>();
        services.TryAddScoped<Workforce.Core.Unavailability.IShiftLookup, Unavailability.NoShiftPlanning>();
        services.AddScoped<Workforce.Core.Unavailability.ShiftCoverage>();
        services.AddScoped<Workforce.Core.Unavailability.UnavailabilityService>();
        services.AddScoped<Workforce.Core.WorkingTime.IWorkingTimeStore, WorkingTime.EfWorkingTimeStore>();
        services.AddScoped<Workforce.Core.WorkingTime.IEmploymentLookup, WorkingTime.EfEmploymentLookup>();
        services.AddScoped<Workforce.Core.WorkingTime.WorkingTimeService>();
        services.AddScoped<AbsenceService>();

        // Medical certificates: encrypted in a separate storage (key from configuration), checked by type and size, reminders and retention in the background.
        var keySetting = $"{WorkforceOptions.ConfigurationSection}:Certificates:EncryptionKey";
        var encryptionKey = configuration[keySetting];
        if (string.IsNullOrWhiteSpace(encryptionKey))
        {
            if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
            {
                throw new InvalidOperationException($"{keySetting} is not configured (base64 32 byte key; User Secrets / Key Vault).");
            }

            encryptionKey = CertificateFileProtector.NewKey(); // temporary key: files do not survive a restart
        }

        services.AddSingleton(CertificateFileProtector.FromConfiguration(encryptionKey, keySetting));
        // The files live apart from the database and from the application files, under random names (a directory only this service can use).
        var storageSetting = $"{WorkforceOptions.ConfigurationSection}:Certificates:StoragePath";
        string storageRoot;
        if (string.IsNullOrWhiteSpace(configuration[storageSetting]))
        {
            if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
            {
                throw new InvalidOperationException($"{storageSetting} is not configured (absolute path of a directory outside the application, only this service may access it).");
            }

            storageRoot = Path.Combine(Path.GetTempPath(), "newworld-workforce-certificates", Guid.NewGuid().ToString("N")); // like the key: gone with the restart
        }
        else
        {
            storageRoot = FileSystemCertificateStorage.ValidateRoot(configuration[storageSetting], environment.ContentRootPath, storageSetting);
        }

        services.AddSingleton<ICertificateFileStorage>(_ => new FileSystemCertificateStorage(storageRoot));
        services.AddScoped<CertificateFileCleanup>();
        services.AddScoped<ICertificateStore, EfCertificateStore>();
        AddMalwareScanner(services, configuration, environment);
        services.AddScoped<CertificateService>();
        services.AddHostedService<CertificateMaintenanceService>();
        services.AddScoped<WorkforceUnitOfWork>();

        // Register the privileges of this API with Access (only if Access is configured).
        if (configuration["Access:BaseUrl"] is { Length: > 0 })
        {
            services.AddCatalogRegistration(
                configuration, AbsencePrivileges.AppKey,
                [.. AbsencePrivileges.Catalog.Concat(CertificatePrivileges.Catalog).Concat(Workforce.Core.Employees.EmployeePrivileges.Catalog).Concat(Workforce.Core.Unavailability.UnavailabilityPrivileges.Catalog).Concat(Workforce.Core.WorkingTime.WorkingTimePrivileges.Catalog).Select(p => new CatalogPrivilege(p.Key, p.Scopable, p.Description))]);
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

    /// <summary>
    /// ClamAV (clamd) checks every upload. Required outside Development/Testing, where without a host the placeholder accepts everything.
    /// </summary>
    private static void AddMalwareScanner(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var scannerOptions = configuration.GetSection(ClamAvOptions.Section).Get<ClamAvOptions>() ?? new ClamAvOptions();
        if (string.IsNullOrWhiteSpace(scannerOptions.Host))
        {
            if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
            {
                throw new InvalidOperationException(
                    $"{ClamAvOptions.Section}:Host is not configured (clamd, reachable only for this service); certificates are not accepted without a malware scan.");
            }

            services.AddSingleton<IMalwareScanner, PassThroughMalwareScanner>();
            return;
        }

        if (scannerOptions.Port is < 1 or > 65535 || scannerOptions.TimeoutSeconds < 1)
        {
            throw new InvalidOperationException($"{ClamAvOptions.Section}: Port or TimeoutSeconds is invalid.");
        }

        services.AddSingleton(scannerOptions);
        services.AddSingleton<ClamAvScanner>();
        services.AddSingleton<IMalwareScanner>(sp => sp.GetRequiredService<ClamAvScanner>());
        services.AddHealthChecks().AddCheck<ClamAvHealthCheck>("malware-scanner");
    }

    private static MessageSource Subscription<TMessage>() => MessageSource.Subscription(MessagingNames.For(typeof(TMessage)), "workforce");

    /// <summary>Applies the migrations in Development/Testing (see <see cref="DatabaseInitializer"/>).</summary>
    public static Task MigrateWorkforceDatabaseAsync(this IServiceProvider services, IHostEnvironment environment) =>
        DatabaseInitializer.MigrateInDevelopmentAsync<WorkforceDbContext>(services, environment);
}
