using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Shared.Messaging;
using Shared.Testing;

namespace Shared.Messaging.Tests;

public sealed class MessagingCleanerTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    // 2028 is a leap year; the retention window crosses 29 Feb.
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2028, 3, 3, 12, 0, 0, TimeSpan.Zero));

    private static OutboxMessage Outbox(DateTimeOffset createdAt, DateTimeOffset? processedAt) => new()
    {
        Id = Guid.NewGuid(),
        MessageType = "ThingChanged",
        Destination = "thing-changed",
        Kind = MessageKind.Event,
        Payload = "{}",
        CorrelationId = Guid.NewGuid(),
        OccurredAt = createdAt,
        ProcessedAt = processedAt,
    };

    [Fact]
    public async Task Removes_only_processed_entries_older_than_the_retention()
    {
        await using var db = await TestDb.CreateAsync(postgres);
        var now = _time.GetUtcNow();
        var oldProcessed = Outbox(now.AddDays(-10), now.AddDays(-8));
        var justInsideRetention = Outbox(now.AddDays(-9), now.AddDays(-7).AddMinutes(1));
        var oldButPending = Outbox(now.AddDays(-30), null);
        db.AddRange(oldProcessed, justInsideRetention, oldButPending);
        db.Add(new InboxMessage { MessageId = Guid.NewGuid(), Consumer = "c", ProcessedAt = now.AddDays(-8) });
        db.Add(new InboxMessage { MessageId = Guid.NewGuid(), Consumer = "c", ProcessedAt = now.AddDays(-1) });
        await db.SaveChangesAsync();

        var cleaner = new MessagingCleaner<TestDbContext>(db, _time, new MessagingOptions());
        var removed = await cleaner.CleanAsync();

        removed.ShouldBe(2);
        await using var check = TestDb.Reopen(db);
        var remaining = await check.Set<OutboxMessage>().Select(m => m.Id).ToListAsync();
        remaining.ShouldBe([justInsideRetention.Id, oldButPending.Id], ignoreOrder: true);
        (await check.Set<InboxMessage>().CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Retention_is_configurable()
    {
        await using var db = await TestDb.CreateAsync(postgres);
        var now = _time.GetUtcNow();
        db.Add(Outbox(now.AddDays(-3), now.AddDays(-2)));
        await db.SaveChangesAsync();

        var cleaner = new MessagingCleaner<TestDbContext>(db, _time, new MessagingOptions { Retention = TimeSpan.FromDays(1) });

        (await cleaner.CleanAsync()).ShouldBe(1);
    }

    [Fact]
    public void Default_retention_is_seven_days()
    {
        new MessagingOptions().Retention.ShouldBe(TimeSpan.FromDays(7));
    }
}
