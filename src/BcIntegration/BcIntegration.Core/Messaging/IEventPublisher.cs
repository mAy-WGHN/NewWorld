using Shared.Messaging;

namespace BcIntegration.Core.Messaging;

/// <summary>Publishes an event together with the current unit of work (outbox; implemented in Infrastructure).</summary>
public interface IEventPublisher
{
    void Publish(IEvent message);
}
