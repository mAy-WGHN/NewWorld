using Shared.Testing;

namespace Workforce.Contracts.Tests;

/// <summary>Wire format of every message Workforce publishes.</summary>
public class SnapshotTests
{
    [Fact]
    public Task EmployeeAvailabilityChanged() => ContractSnapshot.Verify(Samples.EmployeeAvailabilityChanged);

    [Fact]
    public Task ShiftUncovered() => ContractSnapshot.Verify(Samples.ShiftUncovered);

    [Fact]
    public Task SyncAbsenceToBc() => ContractSnapshot.Verify(Samples.SyncAbsenceToBc);
}
