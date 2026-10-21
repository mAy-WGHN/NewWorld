using Shared.Messaging;

namespace Shared.Messaging.Tests;

public class MessagingNamesTests
{
    private sealed record EmployeeChanged;
    private sealed record SyncAbsenceToBc;
    private sealed record EmployeeChangedV2;

    [Theory]
    [InlineData(typeof(EmployeeChanged), "employee-changed")]
    [InlineData(typeof(SyncAbsenceToBc), "sync-absence-to-bc")]
    [InlineData(typeof(EmployeeChangedV2), "employee-changed-v2")]
    public void Destination_is_kebab_case_of_the_type_name(Type type, string expected)
    {
        MessagingNames.For(type).ShouldBe(expected);
    }

    [Fact]
    public void Destination_is_stable_for_the_same_type()
    {
        MessagingNames.For(typeof(EmployeeChanged)).ShouldBe(MessagingNames.For(typeof(EmployeeChanged)));
    }

    [Fact]
    public void Non_message_type_name_with_unsupported_characters_is_rejected()
    {
        Should.Throw<ArgumentException>(() => MessagingNames.FromTypeName("Employee_Changed"));
    }
}
