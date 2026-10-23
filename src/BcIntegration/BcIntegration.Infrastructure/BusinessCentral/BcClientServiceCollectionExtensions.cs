using System.Net;
using Azure.Core;
using BcIntegration.Core.Errors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Shared.Authorization;

namespace BcIntegration.Infrastructure.BusinessCentral;

public static class BcClientServiceCollectionExtensions
{
    /// <summary>Key of the <see cref="TokenCredential"/> used for BC (replaceable in tests).</summary>
    public const string CredentialKey = "bc";

    /// <summary>
    /// The BC client with its pipeline: resilience (retry honouring <c>Retry-After</c>, circuit breaker, timeouts) →
    /// central rate limiter (per attempt) → token → HTTP. Safe requests are retried; writes only on 429 (BC did not
    /// process them), otherwise the message is redelivered and the upsert looks up again.
    /// </summary>
    public static IServiceCollection AddBcClient(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(BcClientOptions.ConfigurationSection);
        services.Configure<BcClientOptions>(section);
        var options = section.Get<BcClientOptions>() ?? new BcClientOptions();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddKeyedSingleton<TokenCredential>(CredentialKey, (_, _) => TokenCredentialFactory.Create(options.Credential));
        services.TryAddSingleton(sp => new BcRequestLimiter(sp.GetRequiredService<IOptions<BcClientOptions>>().Value.RateLimit, sp.GetRequiredService<TimeProvider>()));

        var client = services.AddHttpClient<BcClient>((sp, http) =>
            {
                var current = sp.GetRequiredService<IOptions<BcClientOptions>>().Value;
                if (current.BaseUrl is not null)
                {
                    http.BaseAddress = current.BaseUrl;
                }

                // Timeouts come from the resilience pipeline.
                http.Timeout = Timeout.InfiniteTimeSpan;
            });

        // ServiceDefaults adds the standard resilience handler to every client; BC needs its own pipeline instead.
#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers is marked experimental
        client.RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
        client.AddResilienceHandler("bc", (pipeline, context) =>
            {
                var resilience = context.ServiceProvider.GetRequiredService<IOptions<BcClientOptions>>().Value.Resilience;
                pipeline
                    .AddTimeout(resilience.TotalTimeout)
                    .AddRetry(new HttpRetryStrategyOptions
                    {
                        MaxRetryAttempts = resilience.MaxRetries,
                        Delay = resilience.BaseDelay,
                        BackoffType = DelayBackoffType.Exponential,
                        UseJitter = true,
                        ShouldRetryAfterHeader = true,
                        ShouldHandle = args => ValueTask.FromResult(ShouldRetry(args.Context.GetRequestMessage(), args.Outcome)),
                    })
                    .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions { BreakDuration = resilience.BreakDuration })
                    .AddTimeout(resilience.AttemptTimeout);
            });
        client
            .AddHttpMessageHandler(sp => new BcRateLimitingHandler(sp.GetRequiredService<BcRequestLimiter>()))
            .AddHttpMessageHandler(sp =>
            {
                var current = sp.GetRequiredService<IOptions<BcClientOptions>>().Value;
                return new TokenCredentialHandler(sp.GetRequiredKeyedService<TokenCredential>(CredentialKey), current.Scope, sp.GetRequiredService<TimeProvider>());
            });

        return services;
    }

    private static bool ShouldRetry(HttpRequestMessage? request, Outcome<HttpResponseMessage> outcome)
    {
        var safe = request is not null && (request.Method == HttpMethod.Get || request.Method == HttpMethod.Head);
        if (outcome.Result is { } response)
        {
            var status = (int)response.StatusCode;
            return response.StatusCode == HttpStatusCode.TooManyRequests
                   || (safe && BcErrorClassifier.Classify(status, null).Kind == BcErrorKind.Transient);
        }

        // Exceptions (attempt timeouts, connection failures): only safe requests; for writes the outcome is unknown.
        return safe && outcome.Exception is not null and not OperationCanceledException;
    }
}
