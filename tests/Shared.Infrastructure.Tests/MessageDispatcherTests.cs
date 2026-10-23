using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Infrastructure.Messaging;
using Shared.Messaging;
using Shared.Testing;

namespace Shared.Infrastructure.Tests;

/// <summary>Handler that records calls and writes a Thing; behaviour per test.</summary>
public sealed class SyncThingHandler(TestDbContext db, HandlerProbe probe) : IMessageHandler<SyncThing>
{
    public async Task HandleAsync(SyncThing message, CancellationToken cancellationToken)
    {
        probe.Calls++;
        db.Things.Add(new Thing { Id = Guid.NewGuid(), Name = "handled" });
        await db.SaveChangesAsync(cancellationToken);
        probe.Behaviour?.Invoke();
        probe.Handled.TrySetResult(message);
    }
}

public sealed class HandlerProbe
{
    public int Calls { get; set; }
    public Action? Behaviour { get; set; }
    public TaskCompletionSource<SyncThing> Handled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public sealed class MessageDispatcherTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IDisposable
{
    private readonly HandlerProbe _probe = new();
    private readonly CapturingLoggerProvider _logs = new();

    public void Dispose() => _logs.Dispose();

    private async Task<(MessageDispatcher Dispatcher, ServiceProvider Services, string ConnectionString)> CreateAsync()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(_logs));
        services.AddDbContext<TestDbContext>(o => o.UseNpgsql(connectionString));
        services.AddSingleton(_probe);
        services.AddSingleton(TimeProvider.System);
        services.AddMessageHandler<TestDbContext, SyncThing, SyncThingHandler>(MessageSource.Queue("sync-thing"));
        var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<TestDbContext>().Database.EnsureCreatedAsync();
        }

        return (provider.GetRequiredService<MessageDispatcher>(), provider, connectionString);
    }

    private static InboundMessage Inbound(SyncThing message) =>
        new(message.MessageId.ToString(), nameof(SyncThing), MessageJson.Serialize(message), DeliveryCount: 1);

    private static SyncThing NewCommand() =>
        new(Guid.NewGuid(), Guid.NewGuid(), new DateTimeOffset(2026, 10, 23, 8, 0, 0, TimeSpan.Zero), Guid.NewGuid());

    private static async Task<int> CountThings(string connectionString)
    {
        await using var db = new TestDbContext(new DbContextOptionsBuilder<TestDbContext>().UseNpgsql(connectionString).Options);
        return await db.Things.CountAsync();
    }

    [Fact]
    public async Task Handles_a_message_and_records_it_in_the_inbox()
    {
        var (dispatcher, services, cs) = await CreateAsync();
        await using var _ = services;

        var result = await dispatcher.DispatchAsync(Inbound(NewCommand()), CancellationToken.None);

        result.Outcome.ShouldBe(DispatchOutcome.Completed);
        _probe.Calls.ShouldBe(1);
        (await CountThings(cs)).ShouldBe(1);
    }

    [Fact]
    public async Task Redelivered_message_is_recognised_as_duplicate()
    {
        var (dispatcher, services, cs) = await CreateAsync();
        await using var _ = services;
        var message = Inbound(NewCommand());

        await dispatcher.DispatchAsync(message, CancellationToken.None);
        var second = await dispatcher.DispatchAsync(message with { DeliveryCount = 2 }, CancellationToken.None);

        second.Outcome.ShouldBe(DispatchOutcome.Duplicate);
        _probe.Calls.ShouldBe(1);
        (await CountThings(cs)).ShouldBe(1);
    }

    [Theory]
    [InlineData("NotRegistered", "{}", "UnknownMessageType")]
    [InlineData(nameof(SyncThing), "{not json", "InvalidBody")]
    [InlineData(nameof(SyncThing), "null", "InvalidBody")]
    public async Task Unprocessable_messages_go_to_dead_letter_without_calling_the_handler(string type, string body, string reason)
    {
        var (dispatcher, services, _) = await CreateAsync();
        await using var __ = services;

        var result = await dispatcher.DispatchAsync(new InboundMessage(Guid.NewGuid().ToString(), type, body, 1), CancellationToken.None);

        result.Outcome.ShouldBe(DispatchOutcome.DeadLetter);
        result.Reason.ShouldBe(reason);
        _probe.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Message_without_valid_id_goes_to_dead_letter()
    {
        var (dispatcher, services, _) = await CreateAsync();
        await using var __ = services;

        var result = await dispatcher.DispatchAsync(Inbound(NewCommand()) with { MessageId = "not-a-guid" }, CancellationToken.None);

        result.Outcome.ShouldBe(DispatchOutcome.DeadLetter);
        result.Reason.ShouldBe("InvalidMessageId");
    }

    [Fact]
    public async Task Permanent_failure_rolls_back_and_goes_to_dead_letter_with_its_code()
    {
        var (dispatcher, services, cs) = await CreateAsync();
        await using var _ = services;
        _probe.Behaviour = () => throw new PermanentMessageFailureException("BcUnauthorized");

        var result = await dispatcher.DispatchAsync(Inbound(NewCommand()), CancellationToken.None);

        result.Outcome.ShouldBe(DispatchOutcome.DeadLetter);
        result.Reason.ShouldBe("BcUnauthorized");
        (await CountThings(cs)).ShouldBe(0);
    }

    [Fact]
    public async Task Transient_failure_rolls_back_and_asks_for_redelivery_which_then_succeeds()
    {
        var (dispatcher, services, cs) = await CreateAsync();
        await using var _ = services;
        var message = Inbound(NewCommand());
        _probe.Behaviour = () => throw new HttpRequestException("BC unavailable; body=secret-content");

        var first = await dispatcher.DispatchAsync(message, CancellationToken.None);
        _probe.Behaviour = null;
        var second = await dispatcher.DispatchAsync(message with { DeliveryCount = 2 }, CancellationToken.None);

        first.Outcome.ShouldBe(DispatchOutcome.Retry);
        second.Outcome.ShouldBe(DispatchOutcome.Completed);
        (await CountThings(cs)).ShouldBe(1);
    }

    [Fact]
    public async Task Logs_contain_ids_and_types_but_never_content()
    {
        var (dispatcher, services, _) = await CreateAsync();
        await using var __ = services;
        var command = NewCommand();
        _probe.Behaviour = () => throw new HttpRequestException("body=secret-content");

        await dispatcher.DispatchAsync(Inbound(command), CancellationToken.None);
        await dispatcher.DispatchAsync(new InboundMessage(Guid.NewGuid().ToString(), nameof(SyncThing), "{\"secret\":\"secret-content\"", 1), CancellationToken.None);

        _logs.Lines.ShouldContain(l => l.Contains(command.MessageId.ToString(), StringComparison.Ordinal));
        _logs.Lines.ShouldAllBe(l => !l.Contains("secret-content", StringComparison.Ordinal));
        _logs.Lines.ShouldAllBe(l => !l.Contains(command.ThingId.ToString(), StringComparison.Ordinal));
    }
}

public sealed class CapturingLoggerProvider : ILoggerProvider
{
    public System.Collections.Concurrent.ConcurrentQueue<string> Queue { get; } = new();
    public IReadOnlyCollection<string> Lines => Queue.ToArray();

    public ILogger CreateLogger(string categoryName) => new Logger(Queue);

    public void Dispose()
    {
    }

    private sealed class Logger(System.Collections.Concurrent.ConcurrentQueue<string> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lines.Enqueue(formatter(state, exception));
            if (exception is not null)
            {
                lines.Enqueue(exception.ToString());
            }
        }
    }
}
