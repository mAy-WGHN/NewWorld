using BcIntegration.Contracts;
using Workforce.Contracts;

namespace BcIntegration.Core.Workforce;

/// <summary>Result of a BC write that BC answered. Transient failures throw; permanent technical ones throw <see cref="Errors.BcTechnicalFailureException"/>.</summary>
public abstract record BcWriteResult
{
    public sealed record Succeeded(Guid BcSystemId) : BcWriteResult;

    public sealed record Failed(BcSyncErrorCode Code) : BcWriteResult;
}

/// <summary>Writes an absence to BC as upsert via its external ID (implemented by the BC client).</summary>
public interface IBcAbsenceWriter
{
    Task<BcWriteResult> UpsertAsync(SyncAbsenceToBc absence, CancellationToken cancellationToken);
}

/// <summary>Last version of an absence written to BC.</summary>
public sealed record AbsenceSyncState(Guid AbsenceId, long LastSyncedVersion, Guid BcSystemId);

/// <summary>Entry of the error log (Fehlerprotokoll): codes and IDs only, no content.</summary>
public sealed record SyncError(BcSyncEntityType EntityType, Guid EntityId, long Version, BcSyncErrorCode Code, DateTimeOffset OccurredAt);

/// <summary>
/// Sync state and error log. Changes are committed by the caller's unit of work (the inbox transaction of the
/// message handler), together with the published result event.
/// </summary>
public interface IAbsenceSyncStore
{
    Task<AbsenceSyncState?> GetAsync(Guid absenceId, CancellationToken cancellationToken);

    Task SaveAsync(AbsenceSyncState state, CancellationToken cancellationToken);

    Task AddErrorAsync(SyncError error, CancellationToken cancellationToken);
}
