namespace Shared.Authorization;

/// <summary>
/// Configuration section <c>ServiceAuth</c>: who this service is, the key it signs with, and which other services may call it.
/// <code>
/// ServiceAuth:Name                         = access
/// ServiceAuth:PrivateKey                   = (base64 PKCS#8, secret: User Secrets / Key Vault)
/// ServiceAuth:Peers:workforce:PublicKeys:0 = (base64 SubjectPublicKeyInfo)
/// ServiceAuth:Peers:workforce:Roles:0      = Access.Catalog.Register
/// </code>
/// A service that only calls others needs <c>Name</c> and <c>PrivateKey</c>; a service that is called needs <c>Peers</c>.
/// </summary>
public sealed class ServiceAuthOptions
{
    public const string ConfigurationSection = "ServiceAuth";

    /// <summary>Short name of this service (lower case, e.g. <c>access</c>). Callers address tokens to it.</summary>
    public string? Name { get; set; }

    /// <summary>Private key for signing outgoing tokens: base64 of the PKCS#8 encoding of an ECDSA P-256 key. Never in code or appsettings.</summary>
    public string? PrivateKey { get; set; }

    /// <summary>Services allowed to call this one, by name.</summary>
    public Dictionary<string, ServicePeerOptions> Peers { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>A calling service: its public keys (several while keys are rotated) and the capabilities (service roles) it may use here.</summary>
public sealed class ServicePeerOptions
{
    /// <summary>Base64 of the SubjectPublicKeyInfo of its ECDSA P-256 public key(s).</summary>
    public List<string> PublicKeys { get; set; } = [];

    /// <summary>Service roles of this API granted to the peer (each must be registered via <see cref="SharedAuthorizationOptions.ServiceRoles"/>).</summary>
    public List<string> Roles { get; set; } = [];
}
