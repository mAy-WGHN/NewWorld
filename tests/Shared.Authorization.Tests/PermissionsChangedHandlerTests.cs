using Microsoft.EntityFrameworkCore;
using Shared.Authorization;
using Shared.Testing;

namespace Shared.Authorization.Tests;

public sealed class PermissionDbContext(DbContextOptions<PermissionDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.ConfigurePermissionCache();
}

public sealed class PermissionsChangedHandlerTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly Guid Anna = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Store1 = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid Store2 = Guid.Parse("00000000-0000-0000-0000-0000000000a2");

    private static PermissionSnapshot Snapshot(long version, Guid store) =>
        new(Anna, version, "oid-anna", new HashSet<Guid> { store }, new HashSet<Guid> { Anna });

    private async Task<PermissionDbContext> NewDb()
    {
        var options = new DbContextOptionsBuilder<PermissionDbContext>().UseNpgsql(await postgres.CreateDatabaseAsync()).Options;
        var db = new PermissionDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    [Fact]
    public async Task Newer_snapshot_is_applied_and_persisted()
    {
        await using var db = await NewDb();
        var cache = new InMemoryPermissionCache();
        var handler = new PermissionsChangedHandler(cache, new EfPermissionStore<PermissionDbContext>(db));

        await handler.HandleAsync(Snapshot(1, Store1));
        await handler.HandleAsync(Snapshot(2, Store2));

        cache.TryGet(Anna, out var current);
        current!.StoreIds.ShouldBe([Store2]);
        var rows = await db.Set<PermissionSnapshotRecord>().ToListAsync();
        rows.Single().Version.ShouldBe(2);
    }

    [Fact]
    public async Task Older_snapshot_changes_neither_cache_nor_store()
    {
        await using var db = await NewDb();
        var cache = new InMemoryPermissionCache();
        var handler = new PermissionsChangedHandler(cache, new EfPermissionStore<PermissionDbContext>(db));
        await handler.HandleAsync(Snapshot(5, Store1));

        await handler.HandleAsync(Snapshot(3, Store2));

        cache.TryGet(Anna, out var current);
        current!.StoreIds.ShouldBe([Store1]);
        (await db.Set<PermissionSnapshotRecord>().SingleAsync()).Version.ShouldBe(5);
    }

    [Fact]
    public async Task Cache_is_restored_from_the_store_after_a_restart()
    {
        await using var db = await NewDb();
        var handler = new PermissionsChangedHandler(new InMemoryPermissionCache(), new EfPermissionStore<PermissionDbContext>(db));
        await handler.HandleAsync(Snapshot(7, Store1));

        var restarted = new InMemoryPermissionCache();
        await restarted.LoadAsync(new EfPermissionStore<PermissionDbContext>(db));

        restarted.TryGet(Anna, out var current).ShouldBeTrue();
        current!.Version.ShouldBe(7);
        current.StoreIds.ShouldBe([Store1]);
        restarted.TryResolveEmployeeId("oid-anna", out var id).ShouldBeTrue();
        id.ShouldBe(Anna);
    }
}
