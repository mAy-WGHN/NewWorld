using System.Collections.Concurrent;
using Shared.Infrastructure.Persistence;

namespace Workforce.Api.Tests;

/// <summary>
/// Stands in for the database lock in the tests: rounds of the same name queue up instead of skipping, because the background service ticks when the
/// fake clock moves and a test that calls the round itself must not return before the tick has finished.
/// </summary>
internal sealed class InProcessLock : IDistributedLock
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new();

    public async ValueTask<IAsyncDisposable?> TryAcquireAsync(string name, CancellationToken cancellationToken)
    {
        var gate = _gates.GetOrAdd(name, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Release(gate);
    }

    private sealed class Release(SemaphoreSlim gate) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            gate.Release();
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>Another instance holds every lock: every round is skipped.</summary>
internal sealed class BusyLock : IDistributedLock
{
    public ValueTask<IAsyncDisposable?> TryAcquireAsync(string name, CancellationToken cancellationToken) => ValueTask.FromResult<IAsyncDisposable?>(null);
}
