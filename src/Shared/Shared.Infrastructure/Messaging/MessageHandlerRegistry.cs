using System.Diagnostics.CodeAnalysis;

namespace Shared.Infrastructure.Messaging;

/// <summary>Registered handlers by message type name (the transport's subject).</summary>
public sealed class MessageHandlerRegistry
{
    private readonly Dictionary<string, MessageHandlerRegistration> _byType = new(StringComparer.Ordinal);

    public IReadOnlyCollection<MessageHandlerRegistration> Registrations => _byType.Values;

    internal void Add(MessageHandlerRegistration registration)
    {
        if (!_byType.TryAdd(registration.MessageTypeName, registration))
        {
            throw new InvalidOperationException($"A handler for {registration.MessageTypeName} is already registered.");
        }
    }

    internal bool TryGet(string messageTypeName, [NotNullWhen(true)] out MessageHandlerRegistration? registration) =>
        _byType.TryGetValue(messageTypeName, out registration);
}

/// <summary>One handler registration.</summary>
/// <param name="MessageTypeName">Message type name as sent in the transport's subject.</param>
/// <param name="MessageType">CLR type to deserialize into.</param>
/// <param name="Source">Queue or subscription the handler receives from.</param>
/// <param name="Invoke">Runs the handler for an already deserialized message inside the inbox; <c>false</c> = duplicate.</param>
public sealed record MessageHandlerRegistration(
    string MessageTypeName,
    Type MessageType,
    MessageSource Source,
    Func<IServiceProvider, object, Guid, CancellationToken, Task<bool>> Invoke);
