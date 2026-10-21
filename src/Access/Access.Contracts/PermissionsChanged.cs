using Shared.Messaging;

namespace Access.Contracts;

/// <summary>
/// Complete effective permissions of one person after any change (derived scopes, manual additions, substitutions,
/// expiry, exit). Every API keeps a local copy and checks against it; it never asks the Access service per request.
/// <paramref name="Version"/> rises strictly per person; consumers ignore older or equal versions.
/// </summary>
/// <remarks>Publisher: Access. Consumers: all APIs (via Shared.Authorization).</remarks>
/// <param name="MessageId">Unique message ID (idempotency).</param>
/// <param name="CorrelationId">Correlates all messages of one flow.</param>
/// <param name="OccurredAt">Time of the change (TimeProvider).</param>
/// <param name="EmployeeId">Person the permissions belong to.</param>
/// <param name="Version">Strictly increasing per person.</param>
/// <param name="ExternalIdentities">Identities at the identity provider that sign in as this person (the only place the provider ID travels).</param>
/// <param name="StoreIds">Stores the person may access (Scopes).</param>
/// <param name="EmployeeIds">Employees the person may access, always including themself.</param>
/// <param name="RoleAdditions">
/// Additional roles beyond the token's app roles (e.g. district manager, special rights).
/// TODO(Entscheidung 3): how the fine-grained ACL of the archived Rollen-Rechte maps onto this is still to be designed.
/// </param>
[Sensitivity(SensitivityClass.OrganizationData)]
public sealed record PermissionsChanged(
    Guid MessageId,
    Guid CorrelationId,
    DateTimeOffset OccurredAt,
    Guid EmployeeId,
    long Version,
    IReadOnlyList<ExternalIdentity> ExternalIdentities,
    IReadOnlyList<Guid> StoreIds,
    IReadOnlyList<Guid> EmployeeIds,
    IReadOnlyList<string> RoleAdditions) : IEvent;

/// <summary>An identity at an identity provider, mapped to an EmployeeId by the Access service.</summary>
/// <param name="Provider">Identity provider, e.g. <c>entra</c>.</param>
/// <param name="Subject">The provider's ID of the user (for Entra: the object ID <c>oid</c>).</param>
public sealed record ExternalIdentity(string Provider, string Subject);
