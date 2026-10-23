using BcIntegration.Core.Messaging;
using Shared.Infrastructure.Messaging;
using Shared.Messaging;

namespace BcIntegration.Infrastructure.Messaging;

/// <summary>Publishes through the outbox, i.e. in the same transaction as the state change.</summary>
public sealed class OutboxEventPublisher(Outbox<BcIntegrationDbContext> outbox) : IEventPublisher
{
    public void Publish(IEvent message) => outbox.Publish(message);
}
