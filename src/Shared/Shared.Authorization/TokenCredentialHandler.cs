using System.Net.Http.Headers;
using Azure.Core;

namespace Shared.Authorization;

/// <summary>
/// Adds a bearer token to outgoing service-to-service calls (client credentials / managed identity).
/// Tokens are cached until five minutes before expiry. The credential is chosen by configuration.
/// </summary>
public sealed class TokenCredentialHandler(TokenCredential credential, string scope, TimeProvider time) : DelegatingHandler
{
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private AccessToken? _cached;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await GetTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
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

    private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_cached is { } cached && cached.ExpiresOn - RefreshMargin > time.GetUtcNow())
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
