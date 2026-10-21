using Rollen_Rechte.Authentication;

namespace Rollen_Rechte.Tests.Unit;

public class PrivilegesTests
{
    [Theory]
    [InlineData("user:read", "user:read", true)]
    [InlineData("*", "user:delete", true)]
    [InlineData("user:*", "user:update", true)]
    [InlineData("user:*", "users:read", false)]
    [InlineData("user:*", "role:read", false)]
    [InlineData("user:read", "user:update", false)]
    [InlineData("user:read", "user:read:extra", false)]
    [InlineData("", "user:read", false)]
    public void Allows_MatchesExactWildcardAndResourceWildcard(string granted, string required, bool expected) =>
        Assert.Equal(expected, Privileges.Allows([granted], required));

    [Fact]
    public void Allows_WithNoPrivileges_DeniesEverything() =>
        Assert.False(Privileges.Allows([], "user:read"));
}
