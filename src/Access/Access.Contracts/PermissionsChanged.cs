using Shared.Messaging;

namespace Access.Contracts;

/// <summary>
/// Complete effective permissions of one person after any change (assignment, role, scope, catalogue, start or end of a
/// time-limited assignment, exit). Every API keeps a local copy in its own database and checks against it; it never asks the
/// Access service per request. <paramref name="Version"/> rises strictly per person; consumers ignore older or equal versions.
/// </summary>
/// <remarks>Publisher: Access. Consumers: all APIs (via Shared.Authorization).</remarks>
/// <param name="MessageId">Unique message ID (idempotency).</param>
/// <param name="CorrelationId">Correlates all messages of one flow.</param>
/// <param name="OccurredAt">Time of the change (TimeProvider).</param>
/// <param name="EmployeeId">Person the permissions belong to.</param>
/// <param name="Version">Strictly increasing per person.</param>
/// <param name="ExternalIdentities">Reserved; empty since decision 13 (users sign in through Access, the token carries the EmployeeId).</param>
/// <param name="StoreIds">Union of the stores in the reach of all privileges (overview; checks use <paramref name="Privileges"/>).</param>
/// <param name="EmployeeIds">Union of the employees in the reach of all privileges, always including the person themself.</param>
/// <param name="RoleAdditions">Names of the ACL roles currently assigned to the person (informational, e.g. for display).</param>
/// <param name="Privileges">Effective privileges with their reach; privileges not listed are not granted.</param>
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
    IReadOnlyList<string> RoleAdditions,
    IReadOnlyList<PrivilegeGrant> Privileges) : IEvent;

/// <summary>An identity at an identity provider, mapped to an EmployeeId by the Access service.</summary>
/// <param name="Provider">Identity provider.</param>
/// <param name="Subject">The provider's ID of the user.</param>
public sealed record ExternalIdentity(string Provider, string Subject);

/// <summary>One effective privilege and where it applies.</summary>
/// <param name="Key">Privilege <c>resource:action</c> from the catalogue, e.g. <c>absence:approve</c> (never a wildcard).</param>
/// <param name="Unrestricted">Applies everywhere; then the lists are empty.</param>
/// <param name="StoreIds">Stores the privilege applies to.</param>
/// <param name="EmployeeIds">Employees the privilege applies to (the employees of those stores). Access to one's own data is a privilege of its own (e.g. <c>absence:read-own</c>).</param>
public sealed record PrivilegeGrant(string Key, bool Unrestricted, IReadOnlyList<Guid> StoreIds, IReadOnlyList<Guid> EmployeeIds);
