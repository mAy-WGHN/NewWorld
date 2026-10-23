using BcIntegration.Contracts;
using BcIntegration.Core.MasterData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Infrastructure.Messaging;
using Shared.Messaging;
using Shared.Testing;
using Workforce.Contracts;

namespace BcIntegration.Infrastructure.Tests;

/// <summary>
/// The whole receive path for <see cref="SyncAbsenceToBc"/>: dispatcher → inbox → handler → BC fake → sync state,
/// error log and result event in the outbox, all in one transaction.
/// </summary>
public sealed class AbsenceSyncIntegrationTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IDisposable
{
    private static readonly Guid AbsenceId = Guid.Parse("0197a000-0000-7000-8000-000000000201");
    private readonly LogCapture _logs = new();

    public void Dispose() => _logs.Dispose();

    private async Task<(BcTestHost Bc, string ConnectionString, Guid EmployeeId)> StartAsync(string scenario)
    {
        var connectionString = await BcDatabase.CreateAsync(postgres);
        var bc = BcTestHost.Start(scenario, connectionString: connectionString, configure: s => s.AddLogging(b => b.AddProvider(_logs)));
        var employeeId = Guid.NewGuid();
        await using var scope = bc.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IMasterDataStore>();
        await store.SaveEmployeeAsync(employeeId, new EmployeeState("10042", "Erika Mustermann", null, null, null, null, null, null), CancellationToken.None);
        await store.SaveChangesAsync(CancellationToken.None);
        return (bc, connectionString, employeeId);
    }

    private static InboundMessage Inbound(Guid employeeId, long version) => Inbound(new SyncAbsenceToBc(
        Guid.NewGuid(), Guid.NewGuid(), new DateTimeOffset(2026, 3, 29, 1, 30, 0, TimeSpan.Zero),
        AbsenceId, employeeId, new DateOnly(2026, 3, 30), null, AbsenceType.Sickness, AbsenceStatus.Reported, version));

    private static InboundMessage Inbound(SyncAbsenceToBc command) =>
        new(command.MessageId.ToString(), nameof(SyncAbsenceToBc), MessageJson.Serialize(command), 1);

    private static async Task<DispatchResult> Dispatch(BcTestHost bc, InboundMessage message) =>
        await bc.Services.GetRequiredService<MessageDispatcher>().DispatchAsync(message, CancellationToken.None);

    private static async Task<List<OutboxMessage>> Outbox(string connectionString)
    {
        await using var db = BcDatabase.Open(connectionString);
        return await db.Set<OutboxMessage>().OrderBy(m => m.OccurredAt).ToListAsync();
    }

    [Fact]
    public async Task New_absence_is_created_in_bc_and_success_is_reported_in_the_same_transaction()
    {
        var (bc, cs, employeeId) = await StartAsync("absence-create");
        await using var _ = bc;

        (await Dispatch(bc, Inbound(employeeId, 1))).Outcome.ShouldBe(DispatchOutcome.Completed);

        bc.RequestCount("POST").ShouldBe(1);
        var posted = bc.Server.LogEntries.Single(e => e.RequestMessage!.Method == "POST").RequestMessage!.Body!;
        posted.ShouldContain("\"employeeNumber\":\"10042\"");
        posted.ShouldContain("\"causeOfAbsenceCode\":\"KRANK\"");
        var outbox = (await Outbox(cs)).ShouldHaveSingleItem();
        outbox.MessageType.ShouldBe(nameof(BcSyncSucceeded));
        MessageJson.Deserialize<BcSyncSucceeded>(outbox.Payload).BcSystemId.ShouldBe(Guid.Parse("8f2c1e4a-5b6d-4c3e-9a1b-2c3d4e5f6a7b"));
        await using var db = BcDatabase.Open(cs);
        (await db.AbsenceSyncStates.SingleAsync()).LastSyncedVersion.ShouldBe(1);
    }

    [Fact]
    public async Task Known_absence_is_updated_with_its_etag()
    {
        var (bc, _, employeeId) = await StartAsync("absence-update");
        await using var __ = bc;

        (await Dispatch(bc, Inbound(employeeId, 2))).Outcome.ShouldBe(DispatchOutcome.Completed);

        bc.RequestCount("PATCH").ShouldBe(1);
        bc.RequestCount("POST").ShouldBe(0);
    }

    [Fact]
    public async Task Duplicate_command_and_outdated_version_cause_exactly_one_bc_write()
    {
        var (bc, cs, employeeId) = await StartAsync("absence-create");
        await using var _ = bc;
        var first = Inbound(employeeId, 3);

        await Dispatch(bc, first);
        (await Dispatch(bc, first with { DeliveryCount = 2 })).Outcome.ShouldBe(DispatchOutcome.Duplicate); // same message again
        (await Dispatch(bc, Inbound(employeeId, 3))).Outcome.ShouldBe(DispatchOutcome.Completed);              // same version, new message
        (await Dispatch(bc, Inbound(employeeId, 2))).Outcome.ShouldBe(DispatchOutcome.Completed);              // outdated version

        bc.RequestCount("POST").ShouldBe(1);
        (await Outbox(cs)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Bc_validation_error_is_reported_as_failure_without_retry_and_without_content()
    {
        var (bc, cs, employeeId) = await StartAsync("absence-validation-error");
        await using var _ = bc;

        (await Dispatch(bc, Inbound(employeeId, 1))).Outcome.ShouldBe(DispatchOutcome.Completed);

        bc.RequestCount("POST").ShouldBe(1);
        var outbox = (await Outbox(cs)).ShouldHaveSingleItem();
        outbox.MessageType.ShouldBe(nameof(BcSyncFailed));
        outbox.Payload.ShouldContain("\"errorCode\":\"ValidationFailed\"");
        outbox.Payload.ShouldNotContain("secret detail");
        await using var db = BcDatabase.Open(cs);
        (await db.SyncErrors.SingleAsync()).ErrorCode.ShouldBe("ValidationFailed");
        (await db.AbsenceSyncStates.CountAsync()).ShouldBe(0);
        _logs.Lines.ShouldAllBe(l => !l.Contains("secret detail", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Throttled_write_is_retried_after_retry_after()
    {
        var (bc, _, employeeId) = await StartAsync("absence-throttled-write");
        await using var __ = bc;

        (await Dispatch(bc, Inbound(employeeId, 1))).Outcome.ShouldBe(DispatchOutcome.Completed);

        bc.RequestCount("POST").ShouldBe(2);
    }

    [Fact]
    public async Task Server_error_on_write_is_not_retried_but_redelivered_and_leaves_no_trace()
    {
        var (bc, cs, employeeId) = await StartAsync("absence-server-error");
        await using var _ = bc;

        (await Dispatch(bc, Inbound(employeeId, 1))).Outcome.ShouldBe(DispatchOutcome.Retry);

        bc.RequestCount("POST").ShouldBe(1); // a write with unknown outcome is not repeated blindly
        (await Outbox(cs)).ShouldBeEmpty();
        await using var db = BcDatabase.Open(cs);
        (await db.AbsenceSyncStates.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Unknown_employee_is_a_business_failure_without_bc_call()
    {
        var (bc, cs, _) = await StartAsync("absence-create");
        await using var __ = bc;

        (await Dispatch(bc, Inbound(Guid.NewGuid(), 1))).Outcome.ShouldBe(DispatchOutcome.Completed);

        bc.Server.LogEntries.ShouldBeEmpty();
        (await Outbox(cs)).Single().Payload.ShouldContain("ReferencedRecordMissing");
    }

    [Fact]
    public async Task Misconfigured_bc_endpoint_goes_to_dead_letter()
    {
        var (bc, cs, employeeId) = await StartAsync("employees-empty"); // no absence endpoint: BC answers 404
        await using var _ = bc;

        var result = await Dispatch(bc, Inbound(employeeId, 1));

        result.Outcome.ShouldBe(DispatchOutcome.DeadLetter);
        result.Reason.ShouldBe("BcEndpointNotFound");
        (await Outbox(cs)).ShouldBeEmpty();
    }
}

internal sealed class LogCapture : ILoggerProvider
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _lines = new();

    public IReadOnlyCollection<string> Lines => _lines.ToArray();

    public ILogger CreateLogger(string categoryName) => new Logger(_lines);

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
