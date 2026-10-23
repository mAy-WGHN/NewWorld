using Access.Core;
using Access.Core.Login;
using Access.Core.Messaging;
using Access.Core.Publishing;
using Access.Infrastructure.Acl;
using Access.Infrastructure.Login;
using Access.Infrastructure.Messaging;
using Access.Infrastructure.People;
using Access.Infrastructure.Publishing;
using Azure.Core;
using BcIntegration.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Authorization;
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
        services.AddAccessLogin(configuration, environment);
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

    /// <summary>Login with personnel number + BC PIN (decision 13): BC check via BC Integration, offline fallback, tokens, sessions.</summary>
    private static IServiceCollection AddAccessLogin(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var login = configuration.GetSection(LoginOptions.ConfigurationSection).Get<LoginOptions>() ?? new LoginOptions();
        var jwt = configuration.GetSection(JwtOptions.ConfigurationSection).Get<JwtOptions>() ?? new JwtOptions();
        var local = environment.IsDevelopment() || environment.IsEnvironment("Testing");
        services.AddSingleton(login);
        services.AddSingleton(jwt);
        services.AddSingleton(new PinHasher(login.PinHashIterations));
        services.AddSingleton(new PinProtector(login.PinEncryptionKey));
        services.AddSingleton(sp => new LoginThrottle(
            sp.GetRequiredService<TimeProvider>(), new LoginThrottleOptions { MaxFailures = login.MaxFailuresPerAccount, Window = login.LockWindow }));
        services.AddSingleton(sp => new AccessTokenIssuer(
            jwt, login, sp.GetRequiredService<TimeProvider>(), allowEphemeralKey: local, sp.GetRequiredService<ILogger<AccessTokenIssuer>>()));

        services.AddScoped<IPersonDirectory, EfPersonDirectory>();
        services.AddScoped<ICredentialStore, EfCredentialStore>();
        services.AddScoped<IRefreshTokenStore, EfRefreshTokenStore>();
        services.AddScoped(sp => new LoginService(
            sp.GetRequiredService<IPersonDirectory>(), sp.GetRequiredService<IBcLoginGateway>(), sp.GetRequiredService<ICredentialStore>(),
            sp.GetRequiredService<PinHasher>(), sp.GetRequiredService<PinProtector>(), sp.GetRequiredService<LoginThrottle>(),
            sp.GetRequiredService<TimeProvider>(), login, sp.GetRequiredService<AccessOptions>().Zone));
        services.AddScoped(sp => new SessionService(
            sp.GetRequiredService<IRefreshTokenStore>(), sp.GetRequiredService<IPersonDirectory>(), sp.GetRequiredService<TimeProvider>(),
            login, sp.GetRequiredService<AccessOptions>().Zone));

        var credentialKind = configuration["BcIntegration:Credential"];
        if (credentialKind == TokenCredentialFactory.LocalFake && !local)
        {
            throw new InvalidOperationException("The local fake credential is only allowed in Development/Testing.");
        }

        services.AddKeyedSingleton<TokenCredential>("bc-integration", (_, _) => TokenCredentialFactory.Create(credentialKind));
        services.AddHttpClient<IBcLoginGateway, BcLoginGateway>((sp, http) =>
                http.BaseAddress = new Uri(configuration["BcIntegration:BaseUrl"] ?? "http://bc-integration-api"))
            .AddHttpMessageHandler(sp => new TokenCredentialHandler(
                sp.GetRequiredKeyedService<TokenCredential>("bc-integration"),
                configuration["BcIntegration:Scope"] ?? "api://bc-integration/.default",
                sp.GetRequiredService<TimeProvider>()));
        return services;
    }

    private static MessageSource Subscription<TMessage>() => MessageSource.Subscription(MessagingNames.For(typeof(TMessage)), SubscriptionName);

    /// <summary>Applies the migrations in Development/Testing (see <see cref="DatabaseInitializer"/>).</summary>
    public static Task MigrateAccessDatabaseAsync(this IServiceProvider services, IHostEnvironment environment) =>
        DatabaseInitializer.MigrateInDevelopmentAsync<AccessDbContext>(services, environment);
}
