namespace Access.Infrastructure.StepUp;

/// <summary>
/// The Microsoft (Entra) account of a person for the second factor. HR maintains the <see cref="UserPrincipalName"/>; the immutable object id
/// (<c>oid</c>) is bound at the first successful step-up and checked from then on (a changed UPN keeps the binding, another oid is refused).
/// Personal data: deleted on erasure and when HR removes the link.
/// </summary>
public sealed class EntraLinkRecord
{
    public Guid EmployeeId { get; set; }
    public required string UserPrincipalName { get; set; }
    public Guid? EntraObjectId { get; set; }
    public DateTimeOffset? BoundAt { get; set; }
    public Guid LinkedBy { get; set; }
    public DateTimeOffset LinkedAt { get; set; }
}

/// <summary>A started step-up, redeemed once by the callback. Only the hash of <c>state</c> is stored; the verifier is needed for the code exchange.</summary>
public sealed class StepUpRequestRecord
{
    public required string StateHash { get; set; }
    public Guid EmployeeId { get; set; }
    public required string Nonce { get; set; }
    public required string CodeVerifier { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>The latest second-factor confirmation of a person (one row per person); it counts until <see cref="ValidUntil"/>.</summary>
public sealed class StepUpConfirmationRecord
{
    public Guid EmployeeId { get; set; }
    public DateTimeOffset ConfirmedAt { get; set; }
    public DateTimeOffset ValidUntil { get; set; }
}
