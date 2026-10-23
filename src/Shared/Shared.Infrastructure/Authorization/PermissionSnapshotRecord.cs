namespace Shared.Infrastructure.Authorization;

/// <summary>Database row of a <c>PermissionSnapshot</c>: the privileges with their reach as JSON text.</summary>
public sealed class PermissionSnapshotRecord
{
    public Guid EmployeeId { get; set; }

    public long Version { get; set; }

    public required string PrivilegesJson { get; set; }
}
