using Shared.Authorization;

namespace Shared.Infrastructure.Authorization;

/// <summary>Database row of a <see cref="PermissionSnapshot"/>; the ID sets are stored as JSON text.</summary>
public sealed class PermissionSnapshotRecord
{
    public Guid EmployeeId { get; set; }

    public long Version { get; set; }

    public string? ExternalId { get; set; }

    public required string StoreIdsJson { get; set; }

    public required string EmployeeIdsJson { get; set; }
}
