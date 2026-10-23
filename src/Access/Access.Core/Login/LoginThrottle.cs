namespace Access.Core.Login;

public sealed class LoginThrottleOptions
{
    public int MaxFailures { get; set; } = 5;

    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(15);
}

/// <summary>
/// Counts failures per key within a sliding window and locks the key when the maximum is reached. In memory, per instance
/// (like the archived service). Time from <see cref="TimeProvider"/>.
/// </summary>
public sealed class LoginThrottle(TimeProvider time, LoginThrottleOptions options)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, List<DateTimeOffset>> _failures = new(StringComparer.Ordinal);

    public bool IsLocked(string key, out DateTimeOffset until) => IsLocked(key, options.MaxFailures, out until);

    /// <summary>Locked once <paramref name="maxFailures"/> failures lie within the window; <paramref name="until"/> is when the oldest leaves it.</summary>
    public bool IsLocked(string key, int maxFailures, out DateTimeOffset until)
    {
        lock (_gate)
        {
            var recent = Recent(key);
            if (recent.Count >= maxFailures)
            {
                until = recent.Min() + options.Window;
                return true;
            }

            until = default;
            return false;
        }
    }

    public void RecordFailure(string key)
    {
        lock (_gate)
        {
            Recent(key).Add(time.GetUtcNow());
        }
    }

    public void RecordSuccess(string key)
    {
        lock (_gate)
        {
            _failures.Remove(key);
        }
    }

    public int Failures(string key)
    {
        lock (_gate)
        {
            return Recent(key).Count;
        }
    }

    private List<DateTimeOffset> Recent(string key)
    {
        var limit = time.GetUtcNow() - options.Window;
        if (!_failures.TryGetValue(key, out var list))
        {
            list = [];
            _failures[key] = list;
        }

        list.RemoveAll(t => t <= limit);
        return list;
    }
}
