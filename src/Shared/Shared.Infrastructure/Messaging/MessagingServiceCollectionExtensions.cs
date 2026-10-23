using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Shared.Infrastructure.Messaging;

public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// Outbox/inbox for one service: <see cref="Outbox{TContext}"/>, dispatcher, cleanup, the Service Bus transport and
    /// the consumer host for handlers registered with <c>AddMessageHandler</c>.
    /// Transport: namespace + <see cref="Azure.Core.TokenCredential"/> from <c>Messaging:ServiceBusNamespace</c>; a connection
    /// string (<c>ConnectionStrings:servicebus</c>, emulator) is accepted in Development/Testing only. If neither is
    /// configured, no transport is registered: messages stay in the outbox and nothing is received.
    /// </summary>
    public static IServiceCollection AddMessaging<TContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment) where TContext : DbContext
    {
        var options = new MessagingOptions();
        configuration.GetSection(MessagingOptions.ConfigurationSection).Bind(options);
        services.AddSingleton(options);

        services.AddScoped<Outbox<TContext>>();
        services.AddScoped<OutboxDispatcher<TContext>>();
        services.AddScoped<InboxProcessor<TContext>>();
        services.AddScoped<MessagingCleaner<TContext>>();
        services.TryAddSingleton(TimeProvider.System);

        var client = ResolveClient(configuration, options, environment);
        if (client is not null)
        {
            services.AddSingleton(client);
            services.AddSingleton<IMessageTransport>(sp => new ServiceBusTransport(sp.GetRequiredService<ServiceBusClient>()));
            services.AddHostedService<OutboxDispatcherService<TContext>>();
            services.AddHostedService<ServiceBusConsumerHost>();
        }

        return services;
    }

    private static ServiceBusClient? ResolveClient(IConfiguration configuration, MessagingOptions options, IHostEnvironment environment)
    {
        var connectionString = configuration.GetConnectionString("servicebus");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
            {
                throw new InvalidOperationException("A Service Bus connection string (shared access key) is only allowed in Development/Testing.");
            }

            return new ServiceBusClient(connectionString);
        }

        return string.IsNullOrWhiteSpace(options.ServiceBusNamespace)
            ? null
            : new ServiceBusClient(options.ServiceBusNamespace, new Azure.Identity.DefaultAzureCredential());
    }
}
