namespace Access.Core.Login;

public sealed class LoginOptions
{
    public const string ConfigurationSection = "Access:Login";

    /// <summary>How long the last verified PIN may stand in for BC during an outage; zero disables the offline login.</summary>
    public TimeSpan OfflineValidity { get; set; } = TimeSpan.FromDays(30);

    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(7);

    /// <summary>PBKDF2-SHA512 iterations for new PIN hashes (older hashes keep verifying and are upgraded at the next login).</summary>
    public int PinHashIterations { get; set; } = 210_000;

    /// <summary>AES-256 key (Base64, 32 bytes) for the reversible PIN storage. From user secrets / Key Vault, never from files in the repository.</summary>
    public string? PinEncryptionKey { get; set; }

    public int MaxFailuresPerAccount { get; set; } = 5;

    public int MaxFailuresPerClient { get; set; } = 20;

    public TimeSpan LockWindow { get; set; } = TimeSpan.FromMinutes(15);
}
