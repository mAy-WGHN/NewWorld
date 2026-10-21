using Shared.Messaging;

namespace Shared.Messaging.Tests;

public class SensitivityTests
{
    [Sensitivity(SensitivityClass.OrganizationData)]
    private sealed record Classified;

    private sealed record Unclassified;

    [Fact]
    public void Class_values_are_explicit_and_unknown_is_zero()
    {
        ((int)SensitivityClass.Unknown).ShouldBe(0);
        ((int)SensitivityClass.OrganizationData).ShouldBe(1);
        ((int)SensitivityClass.SensitiveFacts).ShouldBe(2);
        ((int)SensitivityClass.HighlySensitive).ShouldBe(3);
    }

    [Fact]
    public void Reads_class_from_attribute()
    {
        SensitivityReader.Of(typeof(Classified)).ShouldBe(SensitivityClass.OrganizationData);
    }

    [Fact]
    public void Missing_attribute_yields_unknown()
    {
        SensitivityReader.Of(typeof(Unclassified)).ShouldBe(SensitivityClass.Unknown);
    }
}
