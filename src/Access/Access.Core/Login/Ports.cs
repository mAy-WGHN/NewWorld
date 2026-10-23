using Access.Core.People;

namespace Access.Core.Login;

/// <summary>BC's answer to a PIN check (yes or no); the name is not needed because the person comes from the local copy.</summary>
public sealed record BcPinResult(bool Authenticated);

/// <summary>BC (or BC Integration) cannot answer right now. Carries no details: they could contain the PIN.</summary>
public sealed class BcLoginUnavailableException() : Exception("BC is unavailable.");

/// <summary>Checks personnel number and PIN in BC through BC Integration (the only way to BC).</summary>
public interface IBcLoginGateway
{
    /// <exception cref="BcLoginUnavailableException">BC could not be asked or did not answer usably.</exception>
    Task<BcPinResult> VerifyAsync(string personnelNumber, string pin, CancellationToken cancellationToken);
}

/// <summary>Read access to the copy of people from BC.</summary>
public interface IPersonDirectory
{
    Task<Person?> FindByPersonnelNumberAsync(string personnelNumber, CancellationToken cancellationToken);

    Task<Person?> FindAsync(Guid employeeId, CancellationToken cancellationToken);
}

/// <summary>PIN of a person as stored for the offline login: salted hash plus (if a key is configured) the encrypted PIN.</summary>
public sealed record StoredCredential(string PinHash, string? EncryptedPin, DateTimeOffset VerifiedAt);

public interface ICredentialStore
{
    Task<StoredCredential?> GetAsync(Guid employeeId, CancellationToken cancellationToken);

    Task SaveAsync(Guid employeeId, StoredCredential credential, CancellationToken cancellationToken);
}

/// <summary>A refresh token; only its SHA-256 hash is stored.</summary>
public sealed record RefreshTokenEntry(Guid Id, Guid EmployeeId, string TokenHash, DateTimeOffset ExpiresAt, bool Used);

public interface IRefreshTokenStore
{
    Task AddAsync(RefreshTokenEntry entry, CancellationToken cancellationToken);

    Task<RefreshTokenEntry?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken);

    Task MarkUsedAsync(Guid id, CancellationToken cancellationToken);

    Task RevokeAllAsync(Guid employeeId, CancellationToken cancellationToken);
}
