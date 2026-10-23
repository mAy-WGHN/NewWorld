namespace BcIntegration.Infrastructure.Workforce;

/// <summary>Last version of an absence written to BC.</summary>
public sealed class AbsenceSyncStateRecord
{
    public Guid AbsenceId { get; set; }
    public long LastSyncedVersion { get; set; }
    public Guid BcSystemId { get; set; }
}

/// <summary>Error log (Fehlerprotokoll) of permanent business failures: IDs and codes only.</summary>
public sealed class SyncErrorRecord
{
    public Guid Id { get; set; }
    public required string EntityType { get; set; }
    public Guid EntityId { get; set; }
    public long Version { get; set; }
    public required string ErrorCode { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
