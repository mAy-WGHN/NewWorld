using BcIntegration.Contracts;
using BcIntegration.Core.Workforce;
using Microsoft.Extensions.Time.Testing;
using Workforce.Contracts;

namespace BcIntegration.Core.Tests;

public class AbsenceSynchronizerTests
{
    private static readonly Guid AbsenceId = Guid.Parse("0197a000-0000-7000-8000-000000000201");

    private readonly FakeAbsenceWriter _writer = new();
    private readonly FakeAbsenceSyncStore _store = new();
    private readonly RecordingPublisher _publisher = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 3, 29, 1, 30, 0, TimeSpan.Zero));

    private AbsenceSynchronizer Synchronizer() => new(_writer, _store, _publisher, _time);

    private static SyncAbsenceToBc Command(long version) => new(
        Guid.NewGuid(), Guid.NewGuid(), new DateTimeOffset(2026, 3, 29, 1, 0, 0, TimeSpan.Zero),
        AbsenceId, Guid.NewGuid(), new DateOnly(2026, 3, 30), null, AbsenceType.Sickness, AbsenceStatus.Reported, version);

    [Fact]
    public async Task Writes_a_new_absence_and_reports_success()
    {
        var command = Command(1);

        (await Synchronizer().SyncAsync(command, CancellationToken.None)).ShouldBe(AbsenceSyncOutcome.Written);

        _writer.Calls.ShouldHaveSingleItem();
        _store.States[AbsenceId].LastSyncedVersion.ShouldBe(1);
        var succeeded = _publisher.Of<BcSyncSucceeded>().ShouldHaveSingleItem();
        succeeded.SourceService.ShouldBe(SourceService.Workforce);
        succeeded.EntityType.ShouldBe(BcSyncEntityType.Absence);
        succeeded.EntityId.ShouldBe(AbsenceId);
        succeeded.Version.ShouldBe(1);
        succeeded.CorrelationId.ShouldBe(command.CorrelationId);
        succeeded.OccurredAt.ShouldBe(_time.GetUtcNow());
    }

    [Fact]
    public async Task Same_version_again_causes_no_second_write()
    {
        await Synchronizer().SyncAsync(Command(2), CancellationToken.None);

        (await Synchronizer().SyncAsync(Command(2), CancellationToken.None)).ShouldBe(AbsenceSyncOutcome.AlreadySynced);

        _writer.Calls.Count.ShouldBe(1);
        _publisher.Events.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Outdated_version_is_discarded_without_writing()
    {
        await Synchronizer().SyncAsync(Command(4), CancellationToken.None);

        (await Synchronizer().SyncAsync(Command(3), CancellationToken.None)).ShouldBe(AbsenceSyncOutcome.DiscardedOutdated);

        _writer.Calls.Select(c => c.Version).ShouldBe([4]);
        _store.States[AbsenceId].LastSyncedVersion.ShouldBe(4);
    }

    [Fact]
    public async Task Business_failure_is_reported_logged_and_does_not_advance_the_version()
    {
        _writer.Result = _ => new BcWriteResult.Failed(BcSyncErrorCode.ValidationFailed);

        (await Synchronizer().SyncAsync(Command(1), CancellationToken.None)).ShouldBe(AbsenceSyncOutcome.Failed);

        var failed = _publisher.Of<BcSyncFailed>().ShouldHaveSingleItem();
        failed.ErrorCode.ShouldBe(BcSyncErrorCode.ValidationFailed);
        failed.Version.ShouldBe(1);
        _store.States.ShouldBeEmpty();
        var error = _store.Errors.ShouldHaveSingleItem();
        error.ShouldBe(new SyncError(BcSyncEntityType.Absence, AbsenceId, 1, BcSyncErrorCode.ValidationFailed, _time.GetUtcNow()));
    }

    [Fact]
    public async Task Failed_version_can_be_written_by_a_later_corrected_version()
    {
        _writer.Result = _ => new BcWriteResult.Failed(BcSyncErrorCode.ValidationFailed);
        await Synchronizer().SyncAsync(Command(1), CancellationToken.None);
        _writer.Result = _ => new BcWriteResult.Succeeded(Guid.NewGuid());

        (await Synchronizer().SyncAsync(Command(2), CancellationToken.None)).ShouldBe(AbsenceSyncOutcome.Written);
    }

    [Fact]
    public async Task Transient_failure_propagates_and_publishes_nothing()
    {
        _writer.Result = _ => throw new TimeoutException("BC did not answer");

        await Should.ThrowAsync<TimeoutException>(() => Synchronizer().SyncAsync(Command(1), CancellationToken.None));

        _publisher.Events.ShouldBeEmpty();
        _store.States.ShouldBeEmpty();
    }
}
