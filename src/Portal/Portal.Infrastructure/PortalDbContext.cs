using Microsoft.EntityFrameworkCore;
using Shared.Authorization;
using Shared.Messaging;

namespace Portal.Infrastructure;

/// <summary>Own schema <c>portal</c>; no cross-schema joins.</summary>
public sealed class PortalDbContext(DbContextOptions<PortalDbContext> options) : DbContext(options)
{
    public const string Schema = "portal";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ConfigureMessaging();
        modelBuilder.ConfigurePermissionCache();
    }
}
