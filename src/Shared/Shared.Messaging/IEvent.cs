namespace Shared.Messaging;

/// <summary>A state change published to a topic; any number of subscriptions may consume it.</summary>
public interface IEvent : IMessage;
