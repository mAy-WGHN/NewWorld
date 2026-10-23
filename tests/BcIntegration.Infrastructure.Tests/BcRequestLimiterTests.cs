using BcIntegration.Infrastructure.BusinessCentral;
using Microsoft.Extensions.Time.Testing;

namespace BcIntegration.Infrastructure.Tests;

public sealed class BcRequestLimiterTests : IDisposable
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 23, 8, 0, 0, TimeSpan.Zero));
    private readonly BcRequestLimiter _limiter;

    public BcRequestLimiterTests() =>
        _limiter = new BcRequestLimiter(new BcRateLimitOptions { PermitsPerSecond = 3, MaxConcurrency = 10 }, _time);

    public void Dispose() => _limiter.Dispose();

    [Fact]
    public async Task Never_grants_more_than_the_permits_per_second_to_parallel_requests()
    {
        var acquisitions = Enumerable.Range(0, 10).Select(_ => _limiter.AcquireAsync(BcPriority.Normal, CancellationToken.None)).ToList();

        acquisitions.Count(t => t.IsCompletedSuccessfully).ShouldBe(3);
        _time.Advance(TimeSpan.FromSeconds(1));
        acquisitions.Count(t => t.IsCompletedSuccessfully).ShouldBe(6);
        _time.Advance(TimeSpan.FromMilliseconds(999));
        acquisitions.Count(t => t.IsCompletedSuccessfully).ShouldBe(6);
        _time.Advance(TimeSpan.FromSeconds(2));
        acquisitions.Count(t => t.IsCompletedSuccessfully).ShouldBe(10);

        foreach (var lease in await Task.WhenAll(acquisitions))
        {
            lease.Dispose();
        }
    }

    [Fact]
    public async Task Never_exceeds_the_concurrency_limit()
    {
        using var limiter = new BcRequestLimiter(new BcRateLimitOptions { PermitsPerSecond = 100, MaxConcurrency = 2 }, _time);
        var first = await limiter.AcquireAsync(BcPriority.Normal, CancellationToken.None);
        var second = await limiter.AcquireAsync(BcPriority.Normal, CancellationToken.None);
        var third = limiter.AcquireAsync(BcPriority.Normal, CancellationToken.None);

        third.IsCompleted.ShouldBeFalse();
        first.Dispose();
        (await third.WaitAsync(TimeSpan.FromSeconds(1))).Dispose();
        second.Dispose();
    }

    [Fact]
    public async Task High_priority_requests_are_served_before_waiting_normal_ones()
    {
        using var limiter = new BcRequestLimiter(new BcRateLimitOptions { PermitsPerSecond = 1, MaxConcurrency = 10 }, _time);
        using var blocker = await limiter.AcquireAsync(BcPriority.Normal, CancellationToken.None);
        var normal = limiter.AcquireAsync(BcPriority.Normal, CancellationToken.None);
        var high = limiter.AcquireAsync(BcPriority.High, CancellationToken.None);

        _time.Advance(TimeSpan.FromSeconds(1));

        high.IsCompletedSuccessfully.ShouldBeTrue();
        normal.IsCompleted.ShouldBeFalse();
        _time.Advance(TimeSpan.FromSeconds(1));
        normal.IsCompletedSuccessfully.ShouldBeTrue();
        (await high).Dispose();
        (await normal).Dispose();
    }

    [Fact]
    public async Task Cancelled_waiter_does_not_consume_a_permit()
    {
        var blockers = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => _limiter.AcquireAsync(BcPriority.Normal, CancellationToken.None)));
        using var cts = new CancellationTokenSource();
        var cancelled = _limiter.AcquireAsync(BcPriority.Normal, cts.Token);
        var waiting = _limiter.AcquireAsync(BcPriority.Normal, CancellationToken.None);

        await cts.CancelAsync();
        _time.Advance(TimeSpan.FromSeconds(1));

        await Should.ThrowAsync<OperationCanceledException>(() => cancelled);
        (await waiting.WaitAsync(TimeSpan.FromSeconds(1))).Dispose();
        foreach (var b in blockers)
        {
            b.Dispose();
        }
    }
}
