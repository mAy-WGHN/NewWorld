using Microsoft.EntityFrameworkCore;

namespace Shared.Infrastructure.Messaging;

/// <summary>
/// Runs a handler at most once per (message, consumer). The handler's changes and the inbox entry are
/// committed in one transaction; if the handler throws, both roll back and a redelivery runs it again.
/// </summary>
public sealed class InboxProcessor<TContext>(TContext db, TimeProvider time) where TContext : DbContext
{
    /// <returns><c>false</c> if the message was already handled by this consumer.</returns>
    public async Task<bool> ProcessOnceAsync(
        Guid messageId,
        string consumer,
        Func<CancellationToken, Task> handler,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var seen = await db.Set<InboxMessage>()
                .AnyAsync(m => m.MessageId == messageId && m.Consumer == consumer, cancellationToken);
            if (seen)
            {
                return false;
            }

            await handler(cancellationToken);

            // A concurrent duplicate hits the primary key here, rolls back and is dropped on redelivery.
            db.Set<InboxMessage>().Add(new InboxMessage { MessageId = messageId, Consumer = consumer, ProcessedAt = time.GetUtcNow() });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch
        {
            db.ChangeTracker.Clear();
            throw;
        }
    }
}
