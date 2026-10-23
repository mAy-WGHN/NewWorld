using Access.Core.Acl;

namespace Access.Core.Tests;

public class PrivilegePatternTests
{
    private static readonly string[] Catalog = ["absence:read", "absence:approve", "absences:read", "faq:read"];

    [Theory]
    [InlineData("absence:read", "absence:read", true)]
    [InlineData("absence:read", "absence:approve", false)]
    [InlineData("absence:*", "absence:approve", true)]
    [InlineData("absence:*", "absences:read", false)] // prefix must end at the colon
    [InlineData("*", "faq:read", true)]
    public void Matches_exactly_or_by_wildcard(string pattern, string key, bool expected) =>
        PrivilegePattern.Matches(pattern, key).ShouldBe(expected);

    [Fact]
    public void Expansion_yields_only_registered_privileges()
    {
        PrivilegePattern.Expand(["absence:*", "unknown:read"], Catalog).ShouldBe(["absence:read", "absence:approve"], ignoreOrder: true);
        PrivilegePattern.Expand(["*"], Catalog).Count.ShouldBe(4);
    }

    [Theory]
    [InlineData("absence:read", true)]
    [InlineData("absence:*", true)]
    [InlineData("*", true)]
    [InlineData("absence", false)]
    [InlineData("Absence:Read", false)]
    [InlineData("absence:read:x", true)]
    [InlineData("absence:", false)]
    public void Validates_the_format(string pattern, bool valid) => PrivilegePattern.IsValidPattern(pattern).ShouldBe(valid);
}
