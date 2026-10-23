using Azure.Core;
using Microsoft.Extensions.Time.Testing;
using Shared.Authorization;

namespace Shared.Authorization.Tests;

public class TokenCredentialHandlerTests
{
    private sealed class CountingCredential(FakeTimeProvider time, TimeSpan lifetime) : TokenCredential
    {
        public int Calls { get; private set; }
        public string[] LastScopes { get; private set; } = [];

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            Issue(requestContext);

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Issue(requestContext));

        private AccessToken Issue(TokenRequestContext context)
        {
            Calls++;
            LastScopes = context.Scopes;
            return new AccessToken($"token-{Calls}", time.GetUtcNow() + lifetime);
        }
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<string?> AuthorizationHeaders { get; } = [];

        /// <summary>Status codes to answer with, in order; afterwards 200.</summary>
        public Queue<System.Net.HttpStatusCode> Responses { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            AuthorizationHeaders.Add(request.Headers.Authorization?.ToString());
            var status = Responses.TryDequeue(out var next) ? next : System.Net.HttpStatusCode.OK;
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }

    private static (HttpClient Client, CountingCredential Credential, CapturingHandler Inner) Create(FakeTimeProvider time, TimeSpan lifetime)
    {
        var credential = new CountingCredential(time, lifetime);
        var inner = new CapturingHandler();
        var handler = new TokenCredentialHandler(credential, "api://bc-integration/.default", time) { InnerHandler = inner };
        return (new HttpClient(handler), credential, inner);
    }

    [Fact]
    public async Task Adds_bearer_token_for_the_configured_scope()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 3, 29, 1, 59, 0, TimeSpan.Zero));
        var (client, credential, inner) = Create(time, TimeSpan.FromHours(1));

        await client.GetAsync(new Uri("http://localhost/x"));

        inner.AuthorizationHeaders.ShouldBe(["Bearer token-1"]);
        credential.LastScopes.ShouldBe(["api://bc-integration/.default"]);
    }

    [Fact]
    public async Task Token_is_cached_until_shortly_before_expiry()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero));
        var (client, credential, inner) = Create(time, TimeSpan.FromHours(1));

        await client.GetAsync(new Uri("http://localhost/x"));
        time.Advance(TimeSpan.FromMinutes(50));
        await client.GetAsync(new Uri("http://localhost/x"));
        credential.Calls.ShouldBe(1);

        time.Advance(TimeSpan.FromMinutes(6)); // inside the 5-minute refresh margin
        await client.GetAsync(new Uri("http://localhost/x"));

        credential.Calls.ShouldBe(2);
        inner.AuthorizationHeaders.ShouldBe(["Bearer token-1", "Bearer token-1", "Bearer token-2"]);
    }

    [Fact]
    public async Task Unauthorized_answer_refreshes_the_token_once_and_repeats_the_request()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 23, 8, 0, 0, TimeSpan.Zero));
        var (client, credential, inner) = Create(time, TimeSpan.FromHours(1));
        inner.Responses.Enqueue(System.Net.HttpStatusCode.Unauthorized); // token revoked or expired early

        using var response = await client.GetAsync(new Uri("http://localhost/x"));

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        credential.Calls.ShouldBe(2);
        inner.AuthorizationHeaders.ShouldBe(["Bearer token-1", "Bearer token-2"]);
    }

    [Fact]
    public async Task Second_unauthorized_answer_is_returned_without_further_retries()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 23, 8, 0, 0, TimeSpan.Zero));
        var (client, credential, inner) = Create(time, TimeSpan.FromHours(1));
        inner.Responses.Enqueue(System.Net.HttpStatusCode.Unauthorized);
        inner.Responses.Enqueue(System.Net.HttpStatusCode.Unauthorized);

        using var response = await client.GetAsync(new Uri("http://localhost/x"));

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.Unauthorized);
        credential.Calls.ShouldBe(2);
        inner.AuthorizationHeaders.Count.ShouldBe(2);
    }
}
