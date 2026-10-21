using Microsoft.EntityFrameworkCore;

namespace Shared.Infrastructure.Messaging;

public static class MessagingModelBuilderExtensions
{
    /// <summary>Maps outbox and inbox tables into the service's own schema (call from <c>OnModelCreating</c>).</summary>
    public static ModelBuilder ConfigureMessaging(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.ToTable("outbox_messages");
            b.HasKey(m => m.Id);
            b.Property(m => m.MessageType).HasMaxLength(200);
            b.Property(m => m.Destination).HasMaxLength(200);
            b.Property(m => m.Kind).HasConversion<string>().HasMaxLength(20);
            b.HasIndex(m => m.ProcessedAt);
        });

        modelBuilder.Entity<InboxMessage>(b =>
        {
            b.ToTable("inbox_messages");
            b.HasKey(m => new { m.MessageId, m.Consumer });
            b.Property(m => m.Consumer).HasMaxLength(200);
            b.HasIndex(m => m.ProcessedAt);
        });

        return modelBuilder;
    }
}
