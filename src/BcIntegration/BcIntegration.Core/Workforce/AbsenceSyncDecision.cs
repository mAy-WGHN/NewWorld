namespace BcIntegration.Core.Workforce;

public enum AbsenceSyncAction
{
    Unknown = 0,
    Upsert = 1,
    AlreadySynced = 2,
    DiscardOutdated = 3,
}

/// <summary>Versions only move forward in BC: older ones are discarded, the same one is not written twice.</summary>
public static class AbsenceSyncDecision
{
    public static AbsenceSyncAction Decide(long? lastSyncedVersion, long incomingVersion) =>
        lastSyncedVersion switch
        {
            null => AbsenceSyncAction.Upsert,
            var last when incomingVersion > last => AbsenceSyncAction.Upsert,
            var last when incomingVersion == last => AbsenceSyncAction.AlreadySynced,
            _ => AbsenceSyncAction.DiscardOutdated,
        };
}
