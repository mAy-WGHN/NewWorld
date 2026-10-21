using Access.Contracts;
using BcIntegration.Contracts;
using Shared.Testing;

namespace Notifications.Contracts.Tests;

/// <summary>Notifications as consumer: messages stay readable when new fields are added.</summary>
public class ConsumedMessagesTests
{
    [Fact]
    public void SendNotification_tolerates_unknown_fields() => ContractSnapshot.AssertToleratesUnknownFields<SendNotification>("Notifications");

    [Fact]
    public void EmployeeDataErasureRequested_tolerates_unknown_fields() => ContractSnapshot.AssertToleratesUnknownFields<EmployeeDataErasureRequested>("BcIntegration");

    [Fact]
    public void PermissionsChanged_tolerates_unknown_fields() => ContractSnapshot.AssertToleratesUnknownFields<PermissionsChanged>("Access");
}
