namespace Access.Infrastructure.Login;

/// <summary>PIN of a person for the offline login: salted hash and (with key) the encrypted PIN. Deleted on erasure.</summary>
public sealed class CredentialRecord
{
    public Guid EmployeeId { get; set; }
    public required string PinHash { get; set; }
    public string? EncryptedPin { get; set; }
    public DateTimeOffset VerifiedAt { get; set; }
}

/// <summary>Refresh token; only the SHA-256 hash is stored.</summary>
public sealed class RefreshTokenRecord
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public required string TokenHash { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public bool Used { get; set; }
}
