using Access.Contracts;
using BcIntegration.Contracts;
using Shared.Testing;

namespace Workforce.Contracts.Tests;

/// <summary>Workforce as consumer: messages from other services stay readable when new fields are added.</summary>
public class ConsumedMessagesTests
{
    [Fact]
    public void EmployeeChanged_tolerates_unknown_fields() => ContractSnapshot.AssertToleratesUnknownFields<EmployeeChanged>("BcIntegration");

    [Fact]
    public void EmployeeLeft_tolerates_unknown_fields() => ContractSnapshot.AssertToleratesUnknownFields<EmployeeLeft>("BcIntegration");

    [Fact]
    public void EmployeeDataErasureRequested_tolerates_unknown_fields() => ContractSnapshot.AssertToleratesUnknownFields<EmployeeDataErasureRequested>("BcIntegration");

    [Fact]
    public void StoreChanged_tolerates_unknown_fields() => ContractSnapshot.AssertToleratesUnknownFields<StoreChanged>("BcIntegration");

    [Fact]
    public void BcSyncSucceeded_tolerates_unknown_fields() => ContractSnapshot.AssertToleratesUnknownFields<BcSyncSucceeded>("BcIntegration");

    [Fact]
    public void BcSyncFailed_tolerates_unknown_fields() => ContractSnapshot.AssertToleratesUnknownFields<BcSyncFailed>("BcIntegration");

    [Fact]
    public void PermissionsChanged_tolerates_unknown_fields() => ContractSnapshot.AssertToleratesUnknownFields<PermissionsChanged>("Access");

    [Fact]
    public void ResponsibilitiesChanged_tolerates_unknown_fields() => ContractSnapshot.AssertToleratesUnknownFields<ResponsibilitiesChanged>("Access");
}
