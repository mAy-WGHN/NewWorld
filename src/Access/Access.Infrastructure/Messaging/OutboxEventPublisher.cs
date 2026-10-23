using Access.Core.Messaging;
using Shared.Infrastructure.Messaging;
using Shared.Messaging;

namespace Access.Infrastructure.Messaging;

/// <summary>Publishes through the outbox, i.e. in the same transaction as the change.</summary>
public sealed class OutboxEventPublisher(Outbox<AccessDbContext> outbox) : IEventPublisher
{
    public void Publish(IEvent message) => outbox.Publish(message);
}
