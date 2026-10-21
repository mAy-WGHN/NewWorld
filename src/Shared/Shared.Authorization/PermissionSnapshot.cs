namespace Shared.Authorization;

/// <summary>
/// Scopes of one person as published by the Access service (<c>PermissionsChanged</c>).
/// <paramref name="Version"/> rises strictly per person; older or equal versions are ignored.
/// </summary>
public sealed record PermissionSnapshot(
    Guid EmployeeId,
    long Version,
    string? ExternalId,
    IReadOnlySet<Guid> StoreIds,
    IReadOnlySet<Guid> EmployeeIds);
