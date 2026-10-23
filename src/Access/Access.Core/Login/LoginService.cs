using Access.Core.People;

namespace Access.Core.Login;

public enum LoginStatus
{
    Unknown = 0,
    Success = 1,

    /// <summary>Unknown person, wrong PIN or empty input: deliberately indistinguishable.</summary>
    InvalidCredentials = 2,

    /// <summary>Too many failures; see <see cref="LoginResult.LockedUntil"/>.</summary>
    Locked = 3,

    /// <summary>BC cannot be asked and no usable stored PIN exists.</summary>
    Unavailable = 4,
}

public sealed record LoginResult(LoginStatus Status, Guid? EmployeeId = null, DateTimeOffset? LockedUntil = null)
{
    public static readonly LoginResult InvalidCredentials = new(LoginStatus.InvalidCredentials);
    public static readonly LoginResult Unavailable = new(LoginStatus.Unavailable);

    public static LoginResult Success(Guid employeeId) => new(LoginStatus.Success, employeeId);
}

/// <summary>
/// Login with personnel number and BC PIN. BC compares the PIN; after every success a salted hash and (with key) the encrypted PIN
/// are stored, so an outage of BC does not lock everybody out: the last verified PIN is accepted for a limited time. Failures are
/// throttled per account and per client. Only active people can log in. The PIN is never logged and never leaves this service
/// except towards BC Integration.
/// </summary>
public sealed class LoginService(
    IPersonDirectory directory,
    IBcLoginGateway bc,
    ICredentialStore credentials,
    PinHasher hasher,
    PinProtector protector,
    LoginThrottle throttle,
    TimeProvider time,
    LoginOptions options,
    TimeZoneInfo zone)
{
    /// <param name="personnelNumber">Personalnummer.</param>
    /// <param name="pin">The BC PIN; never logged.</param>
    /// <param name="clientKey">Identifies the caller (e.g. IP address) for the per-client limit.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<LoginResult> LoginAsync(string personnelNumber, string pin, string clientKey, CancellationToken cancellationToken)
    {
        var number = personnelNumber.Trim();
        var accountKey = "account:" + number;
        var clientThrottleKey = "client:" + clientKey;
        if (IsLocked(accountKey, clientThrottleKey, out var until))
        {
            return new LoginResult(LoginStatus.Locked, LockedUntil: until);
        }

        if (number.Length == 0 || string.IsNullOrWhiteSpace(pin)
            || await directory.FindByPersonnelNumberAsync(number, cancellationToken) is not { } person
            || !person.IsActiveAt(time.GetUtcNow(), zone))
        {
            return Fail(accountKey, clientThrottleKey);
        }

        BcPinResult answer;
        try
        {
            answer = await bc.VerifyAsync(number, pin, cancellationToken);
        }
        catch (BcLoginUnavailableException)
        {
            return await OfflineAsync(person, pin, accountKey, clientThrottleKey, cancellationToken);
        }

        if (!answer.Authenticated)
        {
            return Fail(accountKey, clientThrottleKey);
        }

        await StoreAsync(person.EmployeeId, pin, cancellationToken);
        throttle.RecordSuccess(accountKey);
        return LoginResult.Success(person.EmployeeId);
    }

    private async Task<LoginResult> OfflineAsync(Person person, string pin, string accountKey, string clientKey, CancellationToken cancellationToken)
    {
        var stored = options.OfflineValidity > TimeSpan.Zero ? await credentials.GetAsync(person.EmployeeId, cancellationToken) : null;
        if (stored is null || time.GetUtcNow() - stored.VerifiedAt >= options.OfflineValidity)
        {
            return LoginResult.Unavailable;
        }

        if (!PinHasher.Verify(pin, stored.PinHash))
        {
            return Fail(accountKey, clientKey);
        }

        throttle.RecordSuccess(accountKey);
        return LoginResult.Success(person.EmployeeId);
    }

    private async Task StoreAsync(Guid employeeId, string pin, CancellationToken cancellationToken)
    {
        var existing = await credentials.GetAsync(employeeId, cancellationToken);
        var samePin = existing is not null && PinHasher.Verify(pin, existing.PinHash);

        // Hashing is deliberately expensive: only redo it if the PIN changed in BC or the hash is outdated.
        var hash = samePin && !hasher.NeedsRehash(existing!.PinHash) ? existing.PinHash : hasher.Hash(pin);
        var encrypted = protector.IsConfigured
            ? (samePin && existing!.EncryptedPin is { } e && protector.Unprotect(e) == pin ? e : protector.Protect(pin))
            : null;
        await credentials.SaveAsync(employeeId, new StoredCredential(hash, encrypted, time.GetUtcNow()), cancellationToken);
    }

    private bool IsLocked(string accountKey, string clientKey, out DateTimeOffset until)
    {
        return throttle.IsLocked(accountKey, options.MaxFailuresPerAccount, out until)
               || throttle.IsLocked(clientKey, options.MaxFailuresPerClient, out until);
    }

    private LoginResult Fail(string accountKey, string clientKey)
    {
        throttle.RecordFailure(accountKey);
        throttle.RecordFailure(clientKey);
        return LoginResult.InvalidCredentials;
    }
}
