namespace Rollen_Rechte.Authentication;

/// <summary>
/// Login-Throttling (Sliding Window, 1 Minute): 5 Versuche je Username+IP, 25 je IP.
/// Nach außen erscheint ein Überschreiten als normaler Login-401.
/// </summary>
public class LoginThrottle(TimeProvider clock)
{
    private const int MaxPerUserAndIp = 5;
    private const int MaxPerIp = 25;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly Dictionary<string, Queue<DateTime>> _hits = new();
    private readonly object _lock = new();

    /// <summary>Zählt den Versuch und liefert false, wenn ein Limit überschritten ist.</summary>
    public bool TryAcquire(string username, string ip)
    {
        var userKey = $"u:{username.ToLowerInvariant()}|{ip}";
        var ipKey = $"i:{ip}";
        var now = clock.GetUtcNow().UtcDateTime;

        lock (_lock)
        {
            if (_hits.Count > 10_000)
                foreach (var key in _hits.Where(kv => Prune(kv.Value, now) == 0).Select(kv => kv.Key).ToList())
                    _hits.Remove(key);

            var userHits = Get(userKey);
            var ipHits = Get(ipKey);
            if (Prune(userHits, now) >= MaxPerUserAndIp || Prune(ipHits, now) >= MaxPerIp) return false;

            userHits.Enqueue(now);
            ipHits.Enqueue(now);
            return true;
        }
    }

    private Queue<DateTime> Get(string key)
    {
        if (!_hits.TryGetValue(key, out var queue)) _hits[key] = queue = new Queue<DateTime>();
        return queue;
    }

    private static int Prune(Queue<DateTime> queue, DateTime now)
    {
        while (queue.Count > 0 && now - queue.Peek() > Window) queue.Dequeue();
        return queue.Count;
    }
}
