using BcIntegration.Core.Workforce;

namespace BcIntegration.Core.Tests;

public class AbsenceSyncDecisionTests
{
    [Fact]
    public void Unknown_absence_is_written() => AbsenceSyncDecision.Decide(lastSyncedVersion: null, incomingVersion: 1).ShouldBe(AbsenceSyncAction.Upsert);

    [Fact]
    public void Newer_version_is_written() => AbsenceSyncDecision.Decide(3, 4).ShouldBe(AbsenceSyncAction.Upsert);

    [Fact]
    public void Same_version_was_already_written() => AbsenceSyncDecision.Decide(4, 4).ShouldBe(AbsenceSyncAction.AlreadySynced);

    [Fact]
    public void Older_version_is_discarded() => AbsenceSyncDecision.Decide(4, 3).ShouldBe(AbsenceSyncAction.DiscardOutdated);
}
