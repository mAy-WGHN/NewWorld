using Microsoft.EntityFrameworkCore;

namespace Shared.Messaging;

/// <summary>Deletes processed outbox and inbox entries older than <see cref="MessagingOptions.Retention"/>.</summary>
public sealed class MessagingCleaner<TContext>(TContext db, TimeProvider time, MessagingOptions options)
    where TContext : DbContext
{
    /// <returns>Number of deleted rows.</returns>
    public async Task<int> CleanAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = time.GetUtcNow() - options.Retention;
        var outbox = await db.Set<OutboxMessage>()
            .Where(m => m.ProcessedAt != null && m.ProcessedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
        var inbox = await db.Set<InboxMessage>()
            .Where(m => m.ProcessedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
        return outbox + inbox;
    }
}
