using Shared.Testing;

namespace Access.Contracts.Tests;

/// <summary>Wire format of every message Access publishes.</summary>
public class SnapshotTests
{
    [Fact]
    public Task PermissionsChanged() => ContractSnapshot.Verify(Samples.PermissionsChanged);
}
