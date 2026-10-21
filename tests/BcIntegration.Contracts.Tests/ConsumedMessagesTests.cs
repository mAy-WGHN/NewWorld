using Shared.Testing;
using Workforce.Contracts;

namespace BcIntegration.Contracts.Tests;

/// <summary>BC Integration as consumer: messages from other services stay readable when new fields are added.</summary>
public class ConsumedMessagesTests
{
    [Fact]
    public void SyncAbsenceToBc_tolerates_unknown_fields() => ContractSnapshot.AssertToleratesUnknownFields<SyncAbsenceToBc>("Workforce");

    [Fact]
    public void PermissionsChanged_tolerates_unknown_fields() => ContractSnapshot.AssertToleratesUnknownFields<Access.Contracts.PermissionsChanged>("Access");
}
