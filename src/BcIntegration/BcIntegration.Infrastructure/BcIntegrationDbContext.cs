using Microsoft.EntityFrameworkCore;
using Shared.Authorization;
using Shared.Messaging;

namespace BcIntegration.Infrastructure;

/// <summary>Own schema <c>bc_integration</c>; no cross-schema joins.</summary>
public sealed class BcIntegrationDbContext(DbContextOptions<BcIntegrationDbContext> options) : DbContext(options)
{
    public const string Schema = "bc_integration";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ConfigureMessaging();
        modelBuilder.ConfigurePermissionCache();
    }
}
