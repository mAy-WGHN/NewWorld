using BcIntegration.Core.Workforce;

namespace BcIntegration.Infrastructure.Workforce;

/// <summary>Changes are committed by the inbox transaction of the message handler.</summary>
public sealed class EfAbsenceSyncStore(BcIntegrationDbContext db) : IAbsenceSyncStore
{
    public async Task<AbsenceSyncState?> GetAsync(Guid absenceId, CancellationToken cancellationToken) =>
        await db.AbsenceSyncStates.FindAsync([absenceId], cancellationToken) is { } r
            ? new AbsenceSyncState(r.AbsenceId, r.LastSyncedVersion, r.BcSystemId)
            : null;

    public async Task SaveAsync(AbsenceSyncState state, CancellationToken cancellationToken)
    {
        var record = await db.AbsenceSyncStates.FindAsync([state.AbsenceId], cancellationToken);
        if (record is null)
        {
            record = new AbsenceSyncStateRecord { AbsenceId = state.AbsenceId };
            db.AbsenceSyncStates.Add(record);
        }

        record.LastSyncedVersion = state.LastSyncedVersion;
        record.BcSystemId = state.BcSystemId;
    }

    public Task AddErrorAsync(SyncError error, CancellationToken cancellationToken)
    {
        db.SyncErrors.Add(new SyncErrorRecord
        {
            Id = Guid.CreateVersion7(error.OccurredAt),
            EntityType = error.EntityType.ToString(),
            EntityId = error.EntityId,
            Version = error.Version,
            ErrorCode = error.Code.ToString(),
            OccurredAt = error.OccurredAt,
        });
        return Task.CompletedTask;
    }
}
