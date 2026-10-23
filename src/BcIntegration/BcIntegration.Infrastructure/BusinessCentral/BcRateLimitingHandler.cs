namespace BcIntegration.Infrastructure.BusinessCentral;

/// <summary>Takes a slot of the central <see cref="BcRequestLimiter"/> for every attempt (retries count, too).</summary>
public sealed class BcRateLimitingHandler(BcRequestLimiter limiter) : DelegatingHandler
{
    public static readonly HttpRequestOptionsKey<BcPriority> PriorityKey = new("bc-priority");

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var priority = request.Options.TryGetValue(PriorityKey, out var p) ? p : BcPriority.Normal;
        using var lease = await limiter.AcquireAsync(priority, cancellationToken);
        return await base.SendAsync(request, cancellationToken);
    }
}
