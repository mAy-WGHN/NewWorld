using Microsoft.EntityFrameworkCore;
using Shared.Authorization;
using Shared.Messaging;

namespace Workforce.Infrastructure;

/// <summary>Own schema <c>workforce</c>; no cross-schema joins.</summary>
public sealed class WorkforceDbContext(DbContextOptions<WorkforceDbContext> options) : DbContext(options)
{
    public const string Schema = "workforce";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ConfigureMessaging();
        modelBuilder.ConfigurePermissionCache();
    }
}
