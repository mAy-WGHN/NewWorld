using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shared.Messaging;

namespace Shared.Infrastructure.Messaging;

public static class MessageHandlerServiceCollectionExtensions
{
    /// <summary>
    /// Registers <typeparamref name="THandler"/> for <typeparamref name="TMessage"/> received from <paramref name="source"/>.
    /// Each subscription is an explicit, documented release (see the message catalogue); the inbox key is the handler name.
    /// </summary>
    public static IServiceCollection AddMessageHandler<TContext, TMessage, THandler>(this IServiceCollection services, MessageSource source)
        where TContext : DbContext
        where TMessage : class, IMessage
        where THandler : class, IMessageHandler<TMessage>
    {
        var registry = services.FirstOrDefault(d => d.ServiceType == typeof(MessageHandlerRegistry))?.ImplementationInstance as MessageHandlerRegistry;
        if (registry is null)
        {
            registry = new MessageHandlerRegistry();
            services.AddSingleton(registry);
        }

        registry.Add(new MessageHandlerRegistration(
            typeof(TMessage).Name,
            typeof(TMessage),
            source,
            static (provider, message, messageId, cancellationToken) =>
            {
                var handler = provider.GetRequiredService<THandler>();
                return provider.GetRequiredService<InboxProcessor<TContext>>()
                    .ProcessOnceAsync(messageId, typeof(THandler).Name, ct => handler.HandleAsync((TMessage)message, ct), cancellationToken);
            }));

        services.TryAddScoped<THandler>();
        services.TryAddScoped<InboxProcessor<TContext>>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<MessageDispatcher>();
        return services;
    }
}
