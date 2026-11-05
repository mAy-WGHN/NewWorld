using System.Text.Json;
using Access.Core.StepUp;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Access.Infrastructure.StepUp;

/// <summary>The OpenID provider cannot be reached or answers nonsense (never carries content of the answer).</summary>
public sealed class EntraUnavailableException(Exception? inner = null) : Exception("The OpenID provider is not available.", inner);

/// <summary>
/// The only code that talks to Microsoft Entra: discovery + signing keys (cached by the configuration manager) and the exchange of the
/// authorization code for the ID token. Uses the named HTTP client <see cref="HttpClientName"/>; the client secret is sent to the token endpoint only.
/// </summary>
public sealed class EntraOidcClient
{
    public const string HttpClientName = "entra";

    private readonly IHttpClientFactory _factory;
    private readonly StepUpOptions _options;
    private readonly ConfigurationManager<OpenIdConnectConfiguration> _configuration;

    public EntraOidcClient(IHttpClientFactory factory, StepUpOptions options)
    {
        _factory = factory;
        _options = options;
        _configuration = new ConfigurationManager<OpenIdConnectConfiguration>(
            $"{options.EffectiveAuthority}/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever(),
            new FactoryDocumentRetriever(factory));
    }

    /// <summary>Discovery document with endpoints and signing keys (cached; refreshed by the manager).</summary>
    public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _configuration.GetConfigurationAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            throw new EntraUnavailableException(ex);
        }
    }

    /// <summary>Asks for a fresh copy of the signing keys at the next use (a key the provider rolled over).</summary>
    public void RequestRefresh() => _configuration.RequestRefresh();

    /// <summary>Exchanges the code for the ID token (<c>null</c> when the provider refuses it).</summary>
    public async Task<string?> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken cancellationToken)
    {
        var configuration = await GetConfigurationAsync(cancellationToken);
        try
        {
            using var http = _factory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, configuration.TokenEndpoint)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = _options.ClientId!,
                    ["client_secret"] = _options.ClientSecret!,
                    ["grant_type"] = "authorization_code",
                    ["code"] = code,
                    ["redirect_uri"] = _options.RedirectUri!,
                    ["code_verifier"] = codeVerifier,
                }),
            };
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return response.StatusCode >= System.Net.HttpStatusCode.InternalServerError ? throw new EntraUnavailableException() : null;
            }

            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            return document.RootElement.TryGetProperty("id_token", out var idToken) && idToken.ValueKind == JsonValueKind.String ? idToken.GetString() : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException
                                       || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new EntraUnavailableException(ex);
        }
    }

    /// <summary>Builds the browser address for the sign-in (authorization code with PKCE S256, <c>prompt=login</c>: no silent single sign-on).</summary>
    public async Task<string> BuildAuthorizationUrlAsync(string state, string nonce, string codeChallenge, CancellationToken cancellationToken)
    {
        var configuration = await GetConfigurationAsync(cancellationToken);
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = _options.ClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = _options.RedirectUri,
            ["response_mode"] = "query",
            ["scope"] = "openid",
            ["state"] = state,
            ["nonce"] = nonce,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["prompt"] = "login",
        };
        return Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(configuration.AuthorizationEndpoint, query);
    }

    private sealed class FactoryDocumentRetriever(IHttpClientFactory factory) : IDocumentRetriever
    {
        public async Task<string> GetDocumentAsync(string address, CancellationToken cancel)
        {
            using var http = factory.CreateClient(HttpClientName);
            return await http.GetStringAsync(address, cancel);
        }
    }
}
