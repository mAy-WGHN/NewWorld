using Access.Contracts;
using BcIntegration.Contracts;
using Shared.Testing;
using Workforce.Contracts;

namespace Portal.Contracts.Tests;

/// <summary>Portal publishes no messages yet; as consumer, messages stay readable when new fields are added.</summary>
public class ConsumedMessagesTests
{
    [Fact]
    public void EmployeeChanged_tolerates_unknown_fields() => ContractSnapshot.AssertToleratesUnknownFields<EmployeeChanged>("BcIntegration");

    [Fact]
    public void EmployeeHired_tolerates_unknown_fields() => ContractSnapshot.AssertToleratesUnknownFields<EmployeeHired>("BcIntegration");

    [Fact]
    public void EmployeeLeft_tolerates_unknown_fields() => ContractSnapshot.AssertToleratesUnknownFields<EmployeeLeft>("BcIntegration");

    [Fact]
    public void EmployeeDataErasureRequested_tolerates_unknown_fields() => ContractSnapshot.AssertToleratesUnknownFields<EmployeeDataErasureRequested>("BcIntegration");

    [Fact]
    public void StoreChanged_tolerates_unknown_fields() => ContractSnapshot.AssertToleratesUnknownFields<StoreChanged>("BcIntegration");

    [Fact]
    public void PermissionsChanged_tolerates_unknown_fields() => ContractSnapshot.AssertToleratesUnknownFields<PermissionsChanged>("Access");

    [Fact]
    public void EmployeeAvailabilityChanged_tolerates_unknown_fields() => ContractSnapshot.AssertToleratesUnknownFields<EmployeeAvailabilityChanged>("Workforce");
}
