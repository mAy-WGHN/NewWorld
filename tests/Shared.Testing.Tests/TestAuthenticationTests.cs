using Shared.Authorization;
using Shared.Testing;

namespace Shared.Testing.Tests;

public class TestAuthenticationTests
{
    private static readonly Guid Anna = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Store = Guid.Parse("00000000-0000-0000-0000-0000000000a1");

    [Fact]
    public void Grant_applies_scopes_and_always_includes_the_person_themself()
    {
        var cache = new InMemoryPermissionCache();

        var snapshot = cache.Grant(Anna, "oid-anna", storeIds: [Store]);

        snapshot.Version.ShouldBe(1);
        snapshot.StoreIds.ShouldBe([Store]);
        snapshot.EmployeeIds.ShouldBe([Anna]);
        cache.TryResolveEmployeeId("oid-anna", out var id).ShouldBeTrue();
        id.ShouldBe(Anna);
    }

    [Fact]
    public void Repeated_grant_raises_the_version_and_replaces_scopes()
    {
        var cache = new InMemoryPermissionCache();
        cache.Grant(Anna, "oid-anna", storeIds: [Store]);

        var second = cache.Grant(Anna, "oid-anna");

        second.Version.ShouldBe(2);
        cache.TryGet(Anna, out var current);
        current!.StoreIds.ShouldBeEmpty();
    }

    [Fact]
    public void AsUser_sets_identity_and_roles_headers()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/x").AsUser("oid-anna", AppRoles.Hr, AppRoles.Employee);

        request.Headers.GetValues(TestAuthHandler.ObjectIdHeader).ShouldBe(["oid-anna"]);
        request.Headers.GetValues(TestAuthHandler.RolesHeader).ShouldBe(["Hr,Employee"]);
    }
}
