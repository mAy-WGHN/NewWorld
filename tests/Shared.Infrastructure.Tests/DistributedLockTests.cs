using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Infrastructure.Persistence;
using Shared.Testing;

namespace Shared.Infrastructure.Tests;

/// <summary>The lock for periodic background work: one holder at a time per name, released on dispose, independent per name.</summary>
public sealed class DistributedLockTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private async Task<ServiceProvider> StartAsync(string? connection = null)
    {
        connection ??= await postgres.CreateDatabaseAsync();
        var services = new ServiceCollection();
        services.AddDbContext<ApprovalDbContext>(options => options.UseNpgsql(connection));
        services.AddPostgresAdvisoryLock<ApprovalDbContext>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Only_one_holder_gets_the_lock_and_it_is_free_again_after_disposing()
    {
        await using var provider = await StartAsync();
        var locks = provider.GetRequiredService<IDistributedLock>();

        var first = await locks.TryAcquireAsync("job", CancellationToken.None);
        first.ShouldNotBeNull();
        (await locks.TryAcquireAsync("job", CancellationToken.None)).ShouldBeNull();

        await first.DisposeAsync();
        var again = await locks.TryAcquireAsync("job", CancellationToken.None);
        again.ShouldNotBeNull();
        await again.DisposeAsync();
    }

    [Fact]
    public async Task Locks_with_different_names_do_not_block_each_other()
    {
        await using var provider = await StartAsync();
        var locks = provider.GetRequiredService<IDistributedLock>();

        await using var a = await locks.TryAcquireAsync("job-a", CancellationToken.None);
        await using var b = await locks.TryAcquireAsync("job-b", CancellationToken.None);

        a.ShouldNotBeNull();
        b.ShouldNotBeNull();
    }

    [Fact]
    public async Task Two_instances_of_a_service_share_the_lock_through_the_database()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var instanceOne = await StartAsync(connection);
        await using var instanceTwo = await StartAsync(connection);

        await using var held = await instanceOne.GetRequiredService<IDistributedLock>().TryAcquireAsync("job", CancellationToken.None);

        held.ShouldNotBeNull();
        (await instanceTwo.GetRequiredService<IDistributedLock>().TryAcquireAsync("job", CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task Work_under_the_lock_runs_in_parallel_rounds_only_once()
    {
        await using var provider = await StartAsync();
        var locks = provider.GetRequiredService<IDistributedLock>();
        var running = 0;
        var maxRunning = 0;
        var executed = 0;

        async Task RoundAsync()
        {
            await using var held = await locks.TryAcquireAsync("job", CancellationToken.None);
            if (held is null)
            {
                return;
            }

            var now = Interlocked.Increment(ref running);
            maxRunning = Math.Max(maxRunning, now);
            Interlocked.Increment(ref executed);
            await Task.Delay(300);
            Interlocked.Decrement(ref running);
        }

        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => RoundAsync()));

        maxRunning.ShouldBe(1);
        executed.ShouldBeGreaterThanOrEqualTo(1);
        executed.ShouldBeLessThan(6);
    }
}
