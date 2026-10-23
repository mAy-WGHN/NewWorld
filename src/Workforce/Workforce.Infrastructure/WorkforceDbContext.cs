using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure.Approvals;
using Workforce.Core.Absences;
using Workforce.Infrastructure.Absences;
using Workforce.Infrastructure.People;
using Shared.Infrastructure.Authorization;
using Shared.Infrastructure.Messaging;

namespace Workforce.Infrastructure;

/// <summary>Own schema <c>workforce</c>; no cross-schema joins.</summary>
public sealed class WorkforceDbContext(DbContextOptions<WorkforceDbContext> options) : DbContext(options)
{
    public const string Schema = "workforce";

    public DbSet<Absence> Absences => Set<Absence>();
    public DbSet<EntitlementRecord> Entitlements => Set<EntitlementRecord>();
    public DbSet<PersonRecord> People => Set<PersonRecord>();
    public DbSet<StoreRecord> Stores => Set<StoreRecord>();
    public DbSet<ApproverCopyRecord> ApproverCopies => Set<ApproverCopyRecord>();

    /// <summary>Provider settings shared by runtime and design time (migrations).</summary>
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string connectionString) => options
        .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", Schema))
        .UseSnakeCaseNamingConvention();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ConfigureMessaging();
        modelBuilder.ConfigurePermissionCache();
        modelBuilder.ConfigureApprovals();

        modelBuilder.Entity<Absence>(b =>
        {
            b.ToTable("absences");
            b.HasKey(a => a.Id);
            b.Ignore(a => a.Range);
            b.Ignore(a => a.IsActive);
            b.Property(a => a.Type).HasConversion<string>().HasMaxLength(20);
            b.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(a => a.SyncStatus).HasConversion<string>().HasMaxLength(20);
            b.HasIndex(a => new { a.EmployeeId, a.From });
            b.HasIndex(a => a.ApprovalRequestId);
        });
        modelBuilder.Entity<EntitlementRecord>(b =>
        {
            b.ToTable("vacation_entitlements");
            b.HasKey(e => new { e.EmployeeId, e.Year });
            b.Property(e => e.Days).HasPrecision(6, 2);
        });
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
        modelBuilder.Entity<ApproverCopyRecord>(b =>
        {
            b.ToTable("approver_copies");
            b.HasKey(a => a.EmployeeId);
        });
    }
}
