using Microsoft.EntityFrameworkCore;
using Shared.Approvals;
using Shared.Infrastructure.Approvals;
using Shared.Testing;

namespace Shared.Infrastructure.Tests;

public sealed class ApprovalDbContext(DbContextOptions<ApprovalDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.ConfigureApprovals();
}

public sealed class ApprovalPersistenceTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Request_with_steps_round_trips_through_the_database()
    {
        var connection = await postgres.CreateDatabaseAsync();
        var options = new DbContextOptionsBuilder<ApprovalDbContext>().UseNpgsql(connection).Options;
        var manager = Guid.NewGuid();
        var director = Guid.NewGuid();
        var now = new DateTimeOffset(2027, 12, 31, 23, 0, 0, TimeSpan.Zero);
        var request = ApprovalRequest.Create("Vacation", Guid.NewGuid(), Guid.NewGuid(), [manager, director], now);
        request.Approve(manager, now.AddHours(2));

        await using (var db = new ApprovalDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            db.Set<ApprovalRequest>().Add(request);
            await db.SaveChangesAsync();
        }

        await using var check = new ApprovalDbContext(options);
        var loaded = await check.Set<ApprovalRequest>().Include(r => r.Steps).SingleAsync();
        loaded.Status.ShouldBe(ApprovalStatus.InReview);
        loaded.SubjectType.ShouldBe("Vacation");
        loaded.Steps.Select(s => s.ApproverIds.Single()).ShouldBe([manager, director]);
        loaded.Steps[0].Decision.ShouldBe(ApprovalDecision.Approved);
        loaded.CurrentApproverIds.ShouldBe([director]);
        loaded.CreatedAt.ShouldBe(now);
    }
}
