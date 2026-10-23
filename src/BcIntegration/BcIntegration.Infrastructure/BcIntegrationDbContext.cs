using BcIntegration.Infrastructure.MasterData;
using BcIntegration.Infrastructure.Workforce;
using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure.Authorization;
using Shared.Infrastructure.Messaging;

namespace BcIntegration.Infrastructure;

/// <summary>Own schema <c>bc_integration</c>; no cross-schema joins.</summary>
public sealed class BcIntegrationDbContext(DbContextOptions<BcIntegrationDbContext> options) : DbContext(options)
{
    public const string Schema = "bc_integration";

    public DbSet<IdMapping> IdMappings => Set<IdMapping>();
    public DbSet<EmployeeStateRecord> EmployeeStates => Set<EmployeeStateRecord>();
    public DbSet<ErasedEmployee> ErasedEmployees => Set<ErasedEmployee>();
    public DbSet<StoreStateRecord> StoreStates => Set<StoreStateRecord>();
    public DbSet<SyncWatermarkRecord> SyncWatermarks => Set<SyncWatermarkRecord>();
    public DbSet<AbsenceSyncStateRecord> AbsenceSyncStates => Set<AbsenceSyncStateRecord>();
    public DbSet<SyncErrorRecord> SyncErrors => Set<SyncErrorRecord>();

    /// <summary>Provider settings shared by runtime and design time (migrations).</summary>
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string connectionString) => options
        .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", Schema))
        .UseSnakeCaseNamingConvention();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ConfigureMessaging();
        modelBuilder.ConfigurePermissionCache();

        modelBuilder.Entity<IdMapping>(b =>
        {
            b.HasKey(m => new { m.EntityType, m.BcSystemId });
            b.HasIndex(m => m.InternalId).IsUnique();
            b.Property(m => m.EntityType).HasMaxLength(20);
        });
        modelBuilder.Entity<EmployeeStateRecord>(b =>
        {
            b.ToTable("employee_states");
            b.HasKey(e => e.EmployeeId);
            b.Property(e => e.PersonnelNumber).HasMaxLength(20);
            b.Property(e => e.Name).HasMaxLength(200);
        });
        modelBuilder.Entity<ErasedEmployee>().HasKey(e => e.EmployeeId);
        modelBuilder.Entity<StoreStateRecord>(b =>
        {
            b.ToTable("store_states");
            b.HasKey(s => s.StoreId);
            b.HasIndex(s => s.StoreNumber);
            b.Property(s => s.StoreNumber).HasMaxLength(20);
            b.Property(s => s.OpeningHoursJson).HasColumnType("jsonb");
        });
        modelBuilder.Entity<SyncWatermarkRecord>(b =>
        {
            b.ToTable("sync_watermarks");
            b.HasKey(w => w.EntityType);
            b.Property(w => w.EntityType).HasMaxLength(20);
        });
        modelBuilder.Entity<AbsenceSyncStateRecord>(b =>
        {
            b.ToTable("absence_sync_states");
            b.HasKey(a => a.AbsenceId);
        });
        modelBuilder.Entity<SyncErrorRecord>(b =>
        {
            b.ToTable("sync_errors");
            b.HasKey(e => e.Id);
            b.HasIndex(e => new { e.EntityType, e.EntityId });
            b.Property(e => e.EntityType).HasMaxLength(20);
            b.Property(e => e.ErrorCode).HasMaxLength(50);
        });
    }
}
