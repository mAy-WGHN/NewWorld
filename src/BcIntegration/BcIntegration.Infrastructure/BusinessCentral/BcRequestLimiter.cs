namespace BcIntegration.Infrastructure.BusinessCentral;

public enum BcPriority
{
    /// <summary>Bulk work such as the master data comparison.</summary>
    Normal = 0,

    /// <summary>Workforce writes (e.g. absences); served before waiting normal requests.</summary>
    High = 1,
}

public sealed class BcRateLimitOptions
{
    /// <summary>Requests started per second (fixed one-second windows). TODO(BC-Modell): confirm BC's limits for our tenant.</summary>
    public int PermitsPerSecond { get; set; } = 5;

    /// <summary>Requests in flight at the same time.</summary>
    public int MaxConcurrency { get; set; } = 5;
}

/// <summary>
/// The one rate limiter for all BC calls of this service (registered as singleton): at most
/// <see cref="BcRateLimitOptions.PermitsPerSecond"/> starts per second and <see cref="BcRateLimitOptions.MaxConcurrency"/>
/// requests in flight. Waiting high-priority requests go first. Time comes from <see cref="TimeProvider"/>.
/// </summary>
public sealed class BcRequestLimiter : IDisposable
{
    private readonly BcRateLimitOptions _options;
    private readonly Lock _gate = new();
    private readonly LinkedList<Waiter> _high = new();
    private readonly LinkedList<Waiter> _normal = new();
    private readonly ITimer _refill;
    private int _permits;
    private int _active;

    public BcRequestLimiter(BcRateLimitOptions options, TimeProvider time)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(options.PermitsPerSecond, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxConcurrency, 1);
        _options = options;
        _permits = options.PermitsPerSecond;
        _refill = time.CreateTimer(_ => Refill(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    /// <summary>Waits for a slot; dispose the result when the request has finished.</summary>
    public Task<IDisposable> AcquireAsync(BcPriority priority, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var queue = priority == BcPriority.High ? _high : _normal;
            var nobodyAhead = _high.Count == 0 && (priority == BcPriority.High || _normal.Count == 0);
            if (nobodyAhead && CanStart())
            {
                return Task.FromResult<IDisposable>(Start());
            }

            var waiter = new Waiter();
            waiter.Node = queue.AddLast(waiter);
            waiter.Registration = cancellationToken.Register(() => Cancel(waiter, cancellationToken));
            return waiter.Completion.Task;
        }
    }

    public void Dispose() => _refill.Dispose();

    private bool CanStart() => _permits > 0 && _active < _options.MaxConcurrency;

    private Lease Start()
    {
        _permits--;
        _active++;
        return new Lease(this);
    }

    private void Refill()
    {
        lock (_gate)
        {
            _permits = _options.PermitsPerSecond;
            GrantWaiting();
        }
    }

    private void Release()
    {
        lock (_gate)
        {
            _active--;
            GrantWaiting();
        }
    }

    private void GrantWaiting()
    {
        while (CanStart() && (_high.First ?? _normal.First) is { } node)
        {
            node.List!.Remove(node);
            node.Value.Registration.Dispose();
            node.Value.Completion.TrySetResult(Start());
        }
    }

    private void Cancel(Waiter waiter, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (waiter.Node?.List is { } list)
            {
                list.Remove(waiter.Node);
                waiter.Completion.TrySetCanceled(cancellationToken);
            }
        }
    }

    private sealed class Waiter
    {
        public TaskCompletionSource<IDisposable> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public LinkedListNode<Waiter>? Node { get; set; }
        public CancellationTokenRegistration Registration { get; set; }
    }

    private sealed class Lease(BcRequestLimiter limiter) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                limiter.Release();
            }
        }
    }
}
