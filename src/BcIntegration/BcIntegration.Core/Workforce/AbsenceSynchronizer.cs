using BcIntegration.Contracts;
using BcIntegration.Core.Messaging;
using Workforce.Contracts;

namespace BcIntegration.Core.Workforce;

public enum AbsenceSyncOutcome
{
    Unknown = 0,
    Written = 1,
    Failed = 2,
    AlreadySynced = 3,
    DiscardedOutdated = 4,
}

/// <summary>
/// Handles <see cref="SyncAbsenceToBc"/>: upsert in BC unless the version is outdated or already written, then reports
/// <see cref="BcSyncSucceeded"/> or (permanent business failure, no retry) <see cref="BcSyncFailed"/> to Workforce.
/// Transient failures propagate so the message is redelivered.
/// </summary>
public sealed class AbsenceSynchronizer(IBcAbsenceWriter writer, IAbsenceSyncStore store, IEventPublisher publisher, TimeProvider time)
{
    public async Task<AbsenceSyncOutcome> SyncAsync(SyncAbsenceToBc command, CancellationToken cancellationToken)
    {
        var state = await store.GetAsync(command.AbsenceId, cancellationToken);
        switch (AbsenceSyncDecision.Decide(state?.LastSyncedVersion, command.Version))
        {
            case AbsenceSyncAction.AlreadySynced:
                return AbsenceSyncOutcome.AlreadySynced;
            case AbsenceSyncAction.DiscardOutdated:
                return AbsenceSyncOutcome.DiscardedOutdated;
        }

        var result = await writer.UpsertAsync(command, cancellationToken);
        var now = time.GetUtcNow();
        switch (result)
        {
            case BcWriteResult.Succeeded succeeded:
                await store.SaveAsync(new AbsenceSyncState(command.AbsenceId, command.Version, succeeded.BcSystemId), cancellationToken);
                publisher.Publish(new BcSyncSucceeded(
                    MessageIds.New(time), command.CorrelationId, now, SourceService.Workforce, BcSyncEntityType.Absence,
                    command.AbsenceId, command.Version, succeeded.BcSystemId));
                return AbsenceSyncOutcome.Written;

            case BcWriteResult.Failed failed:
                await store.AddErrorAsync(new SyncError(BcSyncEntityType.Absence, command.AbsenceId, command.Version, failed.Code, now), cancellationToken);
                publisher.Publish(new BcSyncFailed(
                    MessageIds.New(time), command.CorrelationId, now, SourceService.Workforce, BcSyncEntityType.Absence,
                    command.AbsenceId, command.Version, failed.Code));
                return AbsenceSyncOutcome.Failed;

            default:
                throw new InvalidOperationException($"Unexpected BC write result {result.GetType().Name}.");
        }
    }
}
