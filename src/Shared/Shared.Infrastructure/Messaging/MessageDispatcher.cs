using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Messaging;

namespace Shared.Infrastructure.Messaging;

/// <summary>
/// Transport-independent receive path: finds the handler by message type, deserializes with <see cref="MessageJson"/>
/// and runs the handler once per message (inbox). Logs only type, IDs and exception types, never the body.
/// </summary>
public sealed partial class MessageDispatcher(
    IServiceScopeFactory scopes,
    MessageHandlerRegistry registry,
    ILogger<MessageDispatcher> logger)
{
    public async Task<DispatchResult> DispatchAsync(InboundMessage message, CancellationToken cancellationToken)
    {
        if (message.MessageType is null || !registry.TryGet(message.MessageType, out var registration))
        {
            return DeadLetter(message, "UnknownMessageType");
        }

        if (!Guid.TryParse(message.MessageId, out var messageId))
        {
            return DeadLetter(message, "InvalidMessageId");
        }

        object? body;
        try
        {
            body = JsonSerializer.Deserialize(message.Body, registration.MessageType, MessageJson.Options);
        }
        catch (JsonException)
        {
            body = null;
        }

        if (body is null)
        {
            return DeadLetter(message, "InvalidBody");
        }

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var handled = await registration.Invoke(scope.ServiceProvider, body, messageId, cancellationToken);
            LogHandled(message.MessageType, messageId, handled ? "handled" : "duplicate");
            return new DispatchResult(handled ? DispatchOutcome.Completed : DispatchOutcome.Duplicate);
        }
        catch (PermanentMessageFailureException ex)
        {
            return DeadLetter(message, ex.ReasonCode);
        }
#pragma warning disable CA1031 // any other failure is treated as transient: redelivery, then the transport's dead-letter limit
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
        {
            LogRetry(message.MessageType, messageId, message.DeliveryCount, ex.GetType().Name);
            return new DispatchResult(DispatchOutcome.Retry, ex.GetType().Name);
        }
    }

    private DispatchResult DeadLetter(InboundMessage message, string reason)
    {
        LogDeadLetter(message.MessageType ?? "(none)", message.MessageId ?? "(none)", reason);
        return new DispatchResult(DispatchOutcome.DeadLetter, reason);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "{MessageType} {MessageId}: {Result}")]
    private partial void LogHandled(string messageType, Guid messageId, string result);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{MessageType} {MessageId} failed (delivery {DeliveryCount}), will be redelivered: {ExceptionType}")]
    private partial void LogRetry(string messageType, Guid messageId, int deliveryCount, string exceptionType);

    [LoggerMessage(Level = LogLevel.Error, Message = "{MessageType} {MessageId} moved to dead letter: {Reason}")]
    private partial void LogDeadLetter(string messageType, string messageId, string reason);
}
