using Shared.Testing;

namespace Notifications.Contracts.Tests;

/// <summary>Wire format of every message owned by Notifications (commands it receives from all business APIs).</summary>
public class SnapshotTests
{
    [Fact]
    public Task SendNotification() => ContractSnapshot.Verify(Samples.SendNotification);
}
