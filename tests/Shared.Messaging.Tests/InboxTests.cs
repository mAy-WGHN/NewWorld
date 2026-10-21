using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Shared.Messaging;
using Shared.Testing;

namespace Shared.Messaging.Tests;

public sealed class InboxTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero));

    [Fact]
    public async Task Message_is_processed_once_and_duplicates_are_skipped()
    {
        await using var db = await TestDb.CreateAsync(postgres);
        var inbox = new InboxProcessor<TestDbContext>(db, _time);
        var messageId = Guid.NewGuid();
        var calls = 0;

        var first = await inbox.ProcessOnceAsync(messageId, "consumer-a", _ => { calls++; return Task.CompletedTask; });
        var second = await inbox.ProcessOnceAsync(messageId, "consumer-a", _ => { calls++; return Task.CompletedTask; });

        first.ShouldBeTrue();
        second.ShouldBeFalse();
        calls.ShouldBe(1);
    }

    [Fact]
    public async Task Same_message_is_processed_once_per_consumer()
    {
        await using var db = await TestDb.CreateAsync(postgres);
        var inbox = new InboxProcessor<TestDbContext>(db, _time);
        var messageId = Guid.NewGuid();

        (await inbox.ProcessOnceAsync(messageId, "consumer-a", _ => Task.CompletedTask)).ShouldBeTrue();
        (await inbox.ProcessOnceAsync(messageId, "consumer-b", _ => Task.CompletedTask)).ShouldBeTrue();
    }

    [Fact]
    public async Task Failing_handler_rolls_back_its_changes_and_the_inbox_entry()
    {
        await using var db = await TestDb.CreateAsync(postgres);
        var inbox = new InboxProcessor<TestDbContext>(db, _time);
        var messageId = Guid.NewGuid();

        await Should.ThrowAsync<InvalidOperationException>(() => inbox.ProcessOnceAsync(messageId, "consumer-a", async ct =>
        {
            db.Things.Add(new Thing { Id = Guid.NewGuid(), Name = "half-done" });
            await db.SaveChangesAsync(ct);
            throw new InvalidOperationException("boom");
        }));

        await using var check = TestDb.Reopen(db);
        (await check.Things.CountAsync()).ShouldBe(0);
        (await check.Set<InboxMessage>().CountAsync()).ShouldBe(0);

        // redelivery succeeds
        await using var retry = TestDb.Reopen(db);
        var retryInbox = new InboxProcessor<TestDbContext>(retry, _time);
        (await retryInbox.ProcessOnceAsync(messageId, "consumer-a", _ => Task.CompletedTask)).ShouldBeTrue();
    }
}
