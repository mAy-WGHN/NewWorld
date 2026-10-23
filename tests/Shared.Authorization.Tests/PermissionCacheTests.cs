using Shared.Authorization;

namespace Shared.Authorization.Tests;

public class PermissionCacheTests
{
    private static readonly Guid Anna = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Store1 = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid Store2 = Guid.Parse("00000000-0000-0000-0000-0000000000a2");

    private static PermissionSnapshot Snapshot(long version, Guid store) =>
        new(Anna, version, new Dictionary<string, PrivilegeReach> { ["absence:read"] = PrivilegeReach.Stores([store], []) });

    [Fact]
    public void Newer_version_replaces_older_one()
    {
        var cache = new InMemoryPermissionCache();

        cache.Apply(Snapshot(1, Store1)).ShouldBeTrue();
        cache.Apply(Snapshot(2, Store2)).ShouldBeTrue();

        cache.TryGet(Anna, out var current).ShouldBeTrue();
        current!.Version.ShouldBe(2);
        current.AppliesToStore("absence:read", Store2).ShouldBeTrue();
        current.AppliesToStore("absence:read", Store1).ShouldBeFalse();
    }

    [Fact]
    public void Older_or_equal_version_is_ignored()
    {
        var cache = new InMemoryPermissionCache();
        cache.Apply(Snapshot(5, Store1));

        cache.Apply(Snapshot(4, Store2)).ShouldBeFalse();
        cache.Apply(Snapshot(5, Store2)).ShouldBeFalse();

        cache.TryGet(Anna, out var current);
        current!.AppliesToStore("absence:read", Store1).ShouldBeTrue();
    }

    [Fact]
    public void Unknown_employee_has_no_snapshot()
    {
        new InMemoryPermissionCache().TryGet(Guid.NewGuid(), out _).ShouldBeFalse();
    }

    [Fact]
    public void Unrestricted_privilege_applies_to_every_store_and_employee()
    {
        var snapshot = new PermissionSnapshot(Anna, 1, new Dictionary<string, PrivilegeReach> { ["faq:read"] = PrivilegeReach.Everywhere });

        snapshot.Has("faq:read").ShouldBeTrue();
        snapshot.AppliesToStore("faq:read", Guid.NewGuid()).ShouldBeTrue();
        snapshot.AppliesToEmployee("faq:read", Guid.NewGuid()).ShouldBeTrue();
    }

    [Fact]
    public void Missing_privilege_applies_nowhere()
    {
        var snapshot = Snapshot(1, Store1);

        snapshot.Has("absence:approve").ShouldBeFalse();
        snapshot.AppliesToStore("absence:approve", Store1).ShouldBeFalse();
    }

    [Fact]
    public void Privilege_keys_are_matched_exactly_not_as_patterns()
    {
        var snapshot = new PermissionSnapshot(Anna, 1, new Dictionary<string, PrivilegeReach> { ["absence:*"] = PrivilegeReach.Everywhere });

        snapshot.Has("absence:read").ShouldBeFalse(); // Access expands wildcards against the catalogue before distributing
    }
}
