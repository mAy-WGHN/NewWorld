using System.Text.Json.Nodes;
using Shared.Messaging;

namespace Access.Contracts.Tests;

/// <summary>Fields added later are optional: a message from before the addition is still readable (changes are additive only).</summary>
public class AdditiveChangesTests
{
    [Fact]
    public void PermissionsChanged_from_before_the_main_store_was_added_is_still_readable()
    {
        var json = JsonNode.Parse(MessageJson.Serialize(Samples.PermissionsChanged))!.AsObject();
        json.Remove("mainStoreId").ShouldBeTrue("the field is serialized with its camelCase name");

        var message = MessageJson.Deserialize<PermissionsChanged>(json.ToJsonString());

        message.MainStoreId.ShouldBeNull();
        message.Version.ShouldBe(Samples.PermissionsChanged.Version);
    }
}
