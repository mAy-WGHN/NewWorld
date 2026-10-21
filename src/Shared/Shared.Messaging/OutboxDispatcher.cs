using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Shared.Messaging;

/// <summary>
/// Sends pending outbox messages through the transport. At-least-once: a crash between send and save
/// leads to a duplicate, which receivers drop via <see cref="InboxProcessor{TContext}"/>.
/// Logs only type, IDs and the exception type, never content (exception messages may contain payload).
/// </summary>
public sealed partial class OutboxDispatcher<TContext>(
    TContext db,
    IMessageTransport transport,
    TimeProvider time,
    ILogger<OutboxDispatcher<TContext>> logger) where TContext : DbContext
{
    private const int BatchSize = 100;

    /// <returns>Number of messages sent successfully.</returns>
    public async Task<int> DispatchPendingAsync(CancellationToken cancellationToken = default)
    {
        var pending = await db.Set<OutboxMessage>()
            .Where(m => m.ProcessedAt == null)
            .OrderBy(m => m.OccurredAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        var sent = 0;
        foreach (var entry in pending)
        {
            try
            {
                await transport.SendAsync(
                    new OutboundMessage(entry.Id, entry.CorrelationId, entry.MessageType, entry.Kind, entry.Destination, entry.Payload, entry.OccurredAt),
                    cancellationToken);
                entry.ProcessedAt = time.GetUtcNow();
                sent++;
                LogSent(entry.MessageType, entry.Id, entry.CorrelationId);
            }
#pragma warning disable CA1031 // any transport failure keeps the message pending
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
            {
                entry.Attempts++;
                LogSendFailed(entry.MessageType, entry.Id, entry.CorrelationId, entry.Attempts, ex.GetType().Name);
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        return sent;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Sent {MessageType} {MessageId} (correlation {CorrelationId})")]
    private partial void LogSent(string messageType, Guid messageId, Guid correlationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sending {MessageType} {MessageId} (correlation {CorrelationId}) failed, attempt {Attempts}: {ExceptionType}")]
    private partial void LogSendFailed(string messageType, Guid messageId, Guid correlationId, int attempts, string exceptionType);
}
