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
        ((int)SensitivityClass.SensitiveNeutralized).ShouldBe(2);
        ((int)SensitivityClass.SensitiveFacts).ShouldBe(3);
        ((int)SensitivityClass.HighlySensitive).ShouldBe(4);
    }

    [Theory]
    [InlineData(SensitivityClass.OrganizationData, 1)]
    [InlineData(SensitivityClass.SensitiveNeutralized, 2)]
    [InlineData(SensitivityClass.SensitiveFacts, 2)]
    [InlineData(SensitivityClass.HighlySensitive, 3)]
    public void Maps_to_the_class_number_of_the_privacy_rules(SensitivityClass sensitivity, int expected)
    {
        sensitivity.PrivacyClass().ShouldBe(expected);
    }

    [Fact]
    public void Unknown_has_no_privacy_class()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => SensitivityClass.Unknown.PrivacyClass());
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
