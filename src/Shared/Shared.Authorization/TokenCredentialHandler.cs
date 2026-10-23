using System.Net;
using System.Net.Http.Headers;
using Azure.Core;

namespace Shared.Authorization;

/// <summary>
/// Adds a bearer token to outgoing service-to-service calls (client credentials / managed identity).
/// Tokens are cached until five minutes before expiry; a 401 answer forces one refresh and one repeat.
/// The credential is chosen by configuration.
/// </summary>
public sealed class TokenCredentialHandler(TokenCredential credential, string scope, TimeProvider time) : DelegatingHandler
{
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private AccessToken? _cached;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync(forceRefresh: false, cancellationToken));
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        // The token may have been revoked or expired early: fetch a new one and repeat once.
        response.Dispose();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync(forceRefresh: true, cancellationToken));
        return await base.SendAsync(request, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _gate.Dispose();
        }

        base.Dispose(disposing);
    }

    private async Task<string> GetTokenAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && _cached is { } cached && cached.ExpiresOn - RefreshMargin > time.GetUtcNow())
            {
                return cached.Token;
            }

            var fresh = await credential.GetTokenAsync(new TokenRequestContext([scope]), cancellationToken);
            _cached = fresh;
            return fresh.Token;
        }
        finally
        {
            _gate.Release();
        }
    }
}
