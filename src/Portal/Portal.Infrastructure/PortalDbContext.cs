using Microsoft.EntityFrameworkCore;
using Portal.Infrastructure.Faqs;
using Portal.Infrastructure.Messaging;
using Shared.Infrastructure.Authorization;
using Shared.Infrastructure.Messaging;

namespace Portal.Infrastructure;

/// <summary>Own schema <c>portal</c>; no cross-schema joins.</summary>
public sealed class PortalDbContext(DbContextOptions<PortalDbContext> options) : DbContext(options)
{
    public const string Schema = "portal";

    public DbSet<PersonRecord> People => Set<PersonRecord>();
    public DbSet<StoreRecord> Stores => Set<StoreRecord>();
    public DbSet<FaqRecord> Faqs => Set<FaqRecord>();
    public DbSet<FaqImageRecord> FaqImages => Set<FaqImageRecord>();

    /// <summary>Provider settings shared by runtime and design time (migrations).</summary>
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string connectionString) => options
        .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", Schema))
        .UseSnakeCaseNamingConvention();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ConfigureMessaging();
        modelBuilder.ConfigurePermissionCache();
        modelBuilder.Entity<PersonRecord>(b =>
        {
            b.ToTable("people");
            b.HasKey(p => p.EmployeeId);
            b.HasIndex(p => p.HomeStoreId);
        });
        modelBuilder.Entity<StoreRecord>(b =>
        {
            b.ToTable("stores");
            b.HasKey(s => s.StoreId);
        });
        modelBuilder.Entity<FaqRecord>(b =>
        {
            b.ToTable("faqs");
            b.HasKey(f => f.Id);
            b.Property(f => f.Title).HasMaxLength(255);
            b.HasIndex(f => new { f.Archived, f.SortOrder });
        });
        modelBuilder.Entity<FaqImageRecord>(b =>
        {
            b.ToTable("faq_images");
            b.HasKey(i => i.Id);
            b.Property(i => i.Extension).HasMaxLength(8);
            // Deleting an entry leaves its images orphaned (they can be deleted separately).
            b.HasOne<FaqRecord>().WithMany().HasForeignKey(i => i.FaqId).OnDelete(DeleteBehavior.SetNull);
        });
    }
}
