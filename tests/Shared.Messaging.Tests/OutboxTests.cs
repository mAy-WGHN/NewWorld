using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shared.Messaging;
using Shared.Testing;

namespace Shared.Messaging.Tests;

public sealed class OutboxTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 3, 29, 1, 30, 0, TimeSpan.Zero));

    private ThingChanged NewEvent(Guid thingId) =>
        new(Guid.NewGuid(), Guid.NewGuid(), _time.GetUtcNow(), thingId, "do-not-log-me");

    [Fact]
    public async Task State_change_and_message_are_saved_in_the_same_transaction()
    {
        await using var db = await TestDb.CreateAsync(postgres);
        var outbox = new Outbox<TestDbContext>(db);
        var thing = new Thing { Id = Guid.NewGuid(), Name = "a" };

        db.Things.Add(thing);
        outbox.Publish(NewEvent(thing.Id));
        await db.SaveChangesAsync();

        await using var check = TestDb.Reopen(db);
        (await check.Things.CountAsync()).ShouldBe(1);
        var stored = await check.Set<OutboxMessage>().SingleAsync();
        stored.MessageType.ShouldBe(nameof(ThingChanged));
        stored.Destination.ShouldBe("thing-changed");
        stored.Kind.ShouldBe(MessageKind.Event);
        stored.ProcessedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Rollback_leaves_no_outbox_row_and_nothing_is_sent()
    {
        await using var db = await TestDb.CreateAsync(postgres);
        var outbox = new Outbox<TestDbContext>(db);
        var transport = new RecordingTransport();

        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            var thing = new Thing { Id = Guid.NewGuid(), Name = "a" };
            db.Things.Add(thing);
            outbox.Publish(NewEvent(thing.Id));
            await db.SaveChangesAsync();
            await tx.RollbackAsync();
        }

        await using var check = TestDb.Reopen(db);
        (await check.Things.CountAsync()).ShouldBe(0);
        (await check.Set<OutboxMessage>().CountAsync()).ShouldBe(0);

        var dispatcher = new OutboxDispatcher<TestDbContext>(check, transport, _time, NullLogger<OutboxDispatcher<TestDbContext>>.Instance);
        (await dispatcher.DispatchPendingAsync()).ShouldBe(0);
        transport.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task Commands_go_to_a_queue_destination()
    {
        await using var db = await TestDb.CreateAsync(postgres);
        var outbox = new Outbox<TestDbContext>(db);

        outbox.Send(new SyncThing(Guid.NewGuid(), Guid.NewGuid(), _time.GetUtcNow(), Guid.NewGuid()));
        await db.SaveChangesAsync();

        var stored = await TestDb.Reopen(db).Set<OutboxMessage>().SingleAsync();
        stored.Kind.ShouldBe(MessageKind.Command);
        stored.Destination.ShouldBe("sync-thing");
    }

    [Fact]
    public async Task Dispatcher_sends_pending_messages_once_and_marks_them_processed()
    {
        await using var db = await TestDb.CreateAsync(postgres);
        var outbox = new Outbox<TestDbContext>(db);
        var message = NewEvent(Guid.NewGuid());
        outbox.Publish(message);
        await db.SaveChangesAsync();
        var transport = new RecordingTransport();
        var dispatcher = new OutboxDispatcher<TestDbContext>(db, transport, _time, NullLogger<OutboxDispatcher<TestDbContext>>.Instance);

        (await dispatcher.DispatchPendingAsync()).ShouldBe(1);
        (await dispatcher.DispatchPendingAsync()).ShouldBe(0);

        var sent = transport.Sent.ShouldHaveSingleItem();
        sent.MessageId.ShouldBe(message.MessageId);
        sent.CorrelationId.ShouldBe(message.CorrelationId);
        sent.Destination.ShouldBe("thing-changed");
        sent.Payload.ShouldContain(message.ThingId.ToString());
        var stored = await TestDb.Reopen(db).Set<OutboxMessage>().SingleAsync();
        stored.ProcessedAt.ShouldBe(_time.GetUtcNow());
    }

    [Fact]
    public async Task Failed_send_keeps_message_pending_and_counts_the_attempt()
    {
        await using var db = await TestDb.CreateAsync(postgres);
        new Outbox<TestDbContext>(db).Publish(NewEvent(Guid.NewGuid()));
        await db.SaveChangesAsync();
        var transport = new RecordingTransport { Fail = true };
        var dispatcher = new OutboxDispatcher<TestDbContext>(db, transport, _time, NullLogger<OutboxDispatcher<TestDbContext>>.Instance);

        (await dispatcher.DispatchPendingAsync()).ShouldBe(0);

        var stored = await TestDb.Reopen(db).Set<OutboxMessage>().SingleAsync();
        stored.ProcessedAt.ShouldBeNull();
        stored.Attempts.ShouldBe(1);

        transport.Fail = false;
        (await dispatcher.DispatchPendingAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Dispatcher_logs_never_contain_message_content()
    {
        await using var db = await TestDb.CreateAsync(postgres);
        var message = NewEvent(Guid.NewGuid());
        new Outbox<TestDbContext>(db).Publish(message);
        await db.SaveChangesAsync();
        var logger = new CapturingLogger<OutboxDispatcher<TestDbContext>>();
        var failing = new OutboxDispatcher<TestDbContext>(db, new RecordingTransport { Fail = true }, _time, logger);
        await failing.DispatchPendingAsync();
        var working = new OutboxDispatcher<TestDbContext>(db, new RecordingTransport(), _time, logger);
        await working.DispatchPendingAsync();

        logger.Lines.ShouldNotBeEmpty();
        logger.Lines.ShouldAllBe(line => !line.Contains("do-not-log-me"));
        logger.Lines.ShouldContain(line => line.Contains(message.MessageId.ToString()));
    }
}
