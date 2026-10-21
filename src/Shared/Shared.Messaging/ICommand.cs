namespace Shared.Messaging;

/// <summary>An order sent to a queue; exactly one receiver handles it.</summary>
public interface ICommand : IMessage;
