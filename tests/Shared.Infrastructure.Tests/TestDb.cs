using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure.Messaging;
using Shared.Messaging;
using Shared.Testing;

namespace Shared.Infrastructure.Tests;

public sealed class Thing
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
{
    public DbSet<Thing> Things => Set<Thing>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Thing>().HasKey(t => t.Id);
        modelBuilder.ConfigureMessaging();
    }
}

[Sensitivity(SensitivityClass.OrganizationData)]
public sealed record ThingChanged(Guid MessageId, Guid CorrelationId, DateTimeOffset OccurredAt, Guid ThingId, string Secret)
    : IEvent;

[Sensitivity(SensitivityClass.SensitiveFacts)]
public sealed record SyncThing(Guid MessageId, Guid CorrelationId, DateTimeOffset OccurredAt, Guid ThingId)
    : ICommand;

public static class TestDb
{
    public static async Task<TestDbContext> CreateAsync(PostgresFixture fixture)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseNpgsql(await fixture.CreateDatabaseAsync())
            .Options;
        var db = new TestDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    public static TestDbContext Reopen(TestDbContext db) =>
        new(new DbContextOptionsBuilder<TestDbContext>().UseNpgsql(db.Database.GetConnectionString()!).Options);
}
