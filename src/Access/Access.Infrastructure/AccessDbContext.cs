using Microsoft.EntityFrameworkCore;
using Shared.Authorization;
using Shared.Messaging;

namespace Access.Infrastructure;

/// <summary>Own schema <c>access</c>; no cross-schema joins.</summary>
public sealed class AccessDbContext(DbContextOptions<AccessDbContext> options) : DbContext(options)
{
    public const string Schema = "access";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ConfigureMessaging();
        modelBuilder.ConfigurePermissionCache();
    }
}
