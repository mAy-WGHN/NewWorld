namespace BcIntegration.Core.Login;

/// <summary>Answer of BC to a PIN check: only yes or no and the name. Never carries the PIN.</summary>
public sealed record PinCheck(bool Authenticated, string? EmployeeName);

/// <summary>
/// Checks personnel number and PIN in Business Central, which compares the PIN itself. Empty values are never sent to BC (BC would
/// accept an empty PIN). Transient BC failures throw <see cref="Errors.BcTransientFailureException"/>, permanent technical ones
/// <see cref="Errors.BcTechnicalFailureException"/>; neither contains the PIN.
/// </summary>
public interface IBcPinVerifier
{
    Task<PinCheck> VerifyAsync(string personnelNumber, string pin, CancellationToken cancellationToken);
}
