using System.Threading.RateLimiting;
using Portal.Common.Api;
using Microsoft.AspNetCore.RateLimiting;

namespace Portal.Common.RateLimiting;

public static class RateLimitPolicies
{
    public const string ReportCreation = "report_creation";
}

public static class RateLimitingSetup
{
    private const string TooManyRequests = "Zu viele Anfragen, bitte in Kürze erneut versuchen.";

    // Sliding Window pro Client-IP (hinter Reverse Proxy über UseForwardedHeaders).
    public static IServiceCollection AddIrsRateLimiting(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                HttpMethods.IsOptions(ctx.Request.Method)
                    ? RateLimitPartition.GetNoLimiter("options")
                    : RateLimitPartition.GetSlidingWindowLimiter(ClientIp(ctx), _ => Window(100)));

            options.AddPolicy(RateLimitPolicies.ReportCreation, ctx =>
                RateLimitPartition.GetSlidingWindowLimiter(ClientIp(ctx), _ => Window(10)));

            options.OnRejected = async (context, ct) =>
            {
                var seconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry)
                    ? Math.Max(1, (int)Math.Ceiling(retry.TotalSeconds))
                    : 1;
                context.HttpContext.Response.Headers.RetryAfter = seconds.ToString();
                await ApiErrorWriter.WriteAsync(context.HttpContext, StatusCodes.Status429TooManyRequests, TooManyRequests, ct);
            };
        });

    public static string ClientIp(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static SlidingWindowRateLimiterOptions Window(int limit) => new()
    {
        PermitLimit = limit,
        Window = TimeSpan.FromMinutes(1),
        SegmentsPerWindow = 6,
        QueueLimit = 0
    };
}
