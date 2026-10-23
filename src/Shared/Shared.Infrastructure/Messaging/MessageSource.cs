namespace Shared.Infrastructure.Messaging;

/// <summary>Where a handler receives from: a queue (commands) or a topic subscription (events).</summary>
public sealed record MessageSource(string EntityPath, string? SubscriptionName)
{
    public static MessageSource Queue(string queueName) => new(queueName, null);

    public static MessageSource Subscription(string topicName, string subscriptionName) => new(topicName, subscriptionName);

    public override string ToString() => SubscriptionName is null ? EntityPath : $"{EntityPath}/{SubscriptionName}";
}
