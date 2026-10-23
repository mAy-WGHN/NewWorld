using Shared.Authorization;
using Shared.Testing;

namespace Shared.Testing.Tests;

public class TestAuthenticationTests
{
    private static readonly Guid Anna = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Store = Guid.Parse("00000000-0000-0000-0000-0000000000a1");

    [Fact]
    public void Grant_applies_privileges_with_reach()
    {
        var cache = new InMemoryPermissionCache();

        var snapshot = cache.Grant(Anna, new TestGrant("absence:read", StoreIds: [Store]), TestGrant.Everywhere("faq:read"));

        snapshot.Version.ShouldBe(1);
        snapshot.AppliesToStore("absence:read", Store).ShouldBeTrue();
        snapshot.AppliesToStore("absence:read", Guid.NewGuid()).ShouldBeFalse();
        snapshot.AppliesToStore("faq:read", Guid.NewGuid()).ShouldBeTrue();
    }

    [Fact]
    public void Repeated_grant_raises_the_version_and_replaces_privileges()
    {
        var cache = new InMemoryPermissionCache();
        cache.Grant(Anna, new TestGrant("absence:read", StoreIds: [Store]));

        var second = cache.Grant(Anna);

        second.Version.ShouldBe(2);
        cache.TryGet(Anna, out var current);
        current!.Has("absence:read").ShouldBeFalse();
    }

    [Fact]
    public void AsUser_and_AsService_set_the_test_headers()
    {
        using var user = new HttpRequestMessage(HttpMethod.Get, "/x").AsUser(Anna);
        using var service = new HttpRequestMessage(HttpMethod.Get, "/x").AsService("A.B", "C.D");

        user.Headers.GetValues(TestAuthHandler.EmployeeHeader).ShouldBe([Anna.ToString()]);
        service.Headers.GetValues(TestAuthHandler.ServiceRolesHeader).ShouldBe(["A.B,C.D"]);
        user.Headers.Contains(TestAuthHandler.ServiceRolesHeader).ShouldBeFalse();
    }
}
