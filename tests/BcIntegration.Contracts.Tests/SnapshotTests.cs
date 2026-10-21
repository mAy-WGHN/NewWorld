using Shared.Testing;

namespace BcIntegration.Contracts.Tests;

/// <summary>Wire format of every message BC Integration publishes. A failing snapshot means a breaking change: add a ...V2 message instead.</summary>
public class SnapshotTests
{
    [Fact]
    public Task EmployeeChanged() => ContractSnapshot.Verify(Samples.EmployeeChanged);

    [Fact]
    public Task EmployeeHired() => ContractSnapshot.Verify(Samples.EmployeeHired);

    [Fact]
    public Task EmployeeLeft() => ContractSnapshot.Verify(Samples.EmployeeLeft);

    [Fact]
    public Task EmployeeDataErasureRequested() => ContractSnapshot.Verify(Samples.EmployeeDataErasureRequested);

    [Fact]
    public Task StoreChanged() => ContractSnapshot.Verify(Samples.StoreChanged);

    [Fact]
    public Task BcSyncSucceeded() => ContractSnapshot.Verify(Samples.BcSyncSucceeded);

    [Fact]
    public Task BcSyncFailed() => ContractSnapshot.Verify(Samples.BcSyncFailed);
}
