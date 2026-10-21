using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure.Authorization;
using Shared.Infrastructure.Messaging;

namespace BcIntegration.Infrastructure;

/// <summary>Own schema <c>bc_integration</c>; no cross-schema joins.</summary>
public sealed class BcIntegrationDbContext(DbContextOptions<BcIntegrationDbContext> options) : DbContext(options)
{
    public const string Schema = "bc_integration";

    /// <summary>Provider settings shared by runtime and design time (migrations).</summary>
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string connectionString) => options
        .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", Schema))
        .UseSnakeCaseNamingConvention();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ConfigureMessaging();
        modelBuilder.ConfigurePermissionCache();
    }
}
