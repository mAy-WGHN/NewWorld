using Azure.Core;
using Azure.Identity;

namespace Shared.Authorization;

public static class TokenCredentialFactory
{
    /// <summary>Kind for local fakes (e.g. WireMock as BC): a constant dummy token. Callers must allow it only in Development/Testing.</summary>
    public const string LocalFake = "LocalFake";

    /// <summary>
    /// <c>ManagedIdentity</c> (Azure), <see cref="LocalFake"/> (local fakes only), otherwise <c>DefaultAzureCredential</c>
    /// (local development). Selected by configuration; secrets never live in code or appsettings.
    /// </summary>
    public static TokenCredential Create(string? kind) => kind switch
    {
        "ManagedIdentity" => new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned),
        LocalFake => new StaticTokenCredential("local-fake-token"),
        null or "" or "Default" => new DefaultAzureCredential(),
        _ => throw new ArgumentException($"Unknown credential kind '{kind}'.", nameof(kind)),
    };
}

/// <summary>Constant token that never expires; only for local fakes.</summary>
internal sealed class StaticTokenCredential(string token) : TokenCredential
{
    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => new(token, DateTimeOffset.MaxValue);

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        ValueTask.FromResult(GetToken(requestContext, cancellationToken));
}
