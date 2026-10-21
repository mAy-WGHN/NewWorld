using Shared.Authorization;

namespace Shared.Authorization.Tests;

public class PermissionCacheTests
{
    private static readonly Guid Anna = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Store1 = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid Store2 = Guid.Parse("00000000-0000-0000-0000-0000000000a2");

    private static PermissionSnapshot Snapshot(long version, string? externalId = "oid-anna", params Guid[] stores) =>
        new(Anna, version, externalId, stores.ToHashSet(), new HashSet<Guid> { Anna });

    [Fact]
    public void Newer_version_replaces_older_one()
    {
        var cache = new InMemoryPermissionCache();

        cache.Apply(Snapshot(1, "oid-anna", Store1)).ShouldBeTrue();
        cache.Apply(Snapshot(2, "oid-anna", Store2)).ShouldBeTrue();

        cache.TryGet(Anna, out var current).ShouldBeTrue();
        current!.Version.ShouldBe(2);
        current.StoreIds.ShouldBe([Store2]);
    }

    [Fact]
    public void Older_or_equal_version_is_ignored()
    {
        var cache = new InMemoryPermissionCache();
        cache.Apply(Snapshot(5, "oid-anna", Store1));

        cache.Apply(Snapshot(4, "oid-anna", Store2)).ShouldBeFalse();
        cache.Apply(Snapshot(5, "oid-anna", Store2)).ShouldBeFalse();

        cache.TryGet(Anna, out var current);
        current!.StoreIds.ShouldBe([Store1]);
    }

    [Fact]
    public void External_identity_resolves_to_employee_id()
    {
        var cache = new InMemoryPermissionCache();
        cache.Apply(Snapshot(1, "oid-anna", Store1));

        cache.TryResolveEmployeeId("oid-anna", out var id).ShouldBeTrue();
        id.ShouldBe(Anna);
        cache.TryResolveEmployeeId("oid-unknown", out _).ShouldBeFalse();
    }

    [Fact]
    public void Changed_external_identity_no_longer_resolves_the_old_one()
    {
        var cache = new InMemoryPermissionCache();
        cache.Apply(Snapshot(1, "oid-old", Store1));
        cache.Apply(Snapshot(2, "oid-new", Store1));

        cache.TryResolveEmployeeId("oid-old", out _).ShouldBeFalse();
        cache.TryResolveEmployeeId("oid-new", out var id).ShouldBeTrue();
        id.ShouldBe(Anna);
    }

    [Fact]
    public void Unknown_employee_has_no_snapshot()
    {
        new InMemoryPermissionCache().TryGet(Guid.NewGuid(), out _).ShouldBeFalse();
    }
}
