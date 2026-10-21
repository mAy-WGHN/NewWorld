using Azure.Core;
using Azure.Identity;

namespace Shared.Authorization;

public static class TokenCredentialFactory
{
    /// <summary>
    /// <c>ManagedIdentity</c> (Azure), otherwise <c>DefaultAzureCredential</c> (local development). Selected by
    /// configuration (<c>Authorization:Credential</c>); secrets never live in code or appsettings.
    /// </summary>
    public static TokenCredential Create(string? kind) => kind switch
    {
        "ManagedIdentity" => new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned),
        null or "" or "Default" => new DefaultAzureCredential(),
        _ => throw new ArgumentException($"Unknown credential kind '{kind}'.", nameof(kind)),
    };
}
