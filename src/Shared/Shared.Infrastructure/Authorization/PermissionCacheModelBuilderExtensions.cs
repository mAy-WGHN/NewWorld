using Microsoft.EntityFrameworkCore;

namespace Shared.Infrastructure.Authorization;

public static class PermissionCacheModelBuilderExtensions
{
    /// <summary>Maps the permission cache table into the service's own schema (call from <c>OnModelCreating</c>).</summary>
    public static ModelBuilder ConfigurePermissionCache(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PermissionSnapshotRecord>(b =>
        {
            b.ToTable("permission_snapshots");
            b.HasKey(r => r.EmployeeId);
            b.Property(r => r.ExternalId).HasMaxLength(200);
        });
        return modelBuilder;
    }
}
