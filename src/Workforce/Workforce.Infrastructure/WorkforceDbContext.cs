using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure.Approvals;
using Workforce.Core.Absences;
using Workforce.Core.Certificates;
using Workforce.Infrastructure.Certificates;
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
    public DbSet<StoreSettingsRecord> StoreSettings => Set<StoreSettingsRecord>();
    public DbSet<MainStoreCopyRecord> MainStoreCopies => Set<MainStoreCopyRecord>();
    public DbSet<CertificateDocument> CertificateDocuments => Set<CertificateDocument>();
    public DbSet<CertificateSettingsRecord> CertificateSettings => Set<CertificateSettingsRecord>();
    public DbSet<CertificateReminderSettingsRecord> CertificateReminderSettings => Set<CertificateReminderSettingsRecord>();
    public DbSet<CertificateAccessEntry> CertificateAccessLog => Set<CertificateAccessEntry>();

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
            b.Property(a => a.CertificateStatus).HasConversion<string>().HasMaxLength(20);
            b.HasIndex(a => new { a.EmployeeId, a.From });
            b.HasIndex(a => new { a.Type, a.Status, a.CertificateStatus });
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

        // Medical certificates (AU): metadata (the bytes live in the separate file storage), the settings HR can change, and the access log (who did what, never the content).
        modelBuilder.Entity<CertificateDocument>(b =>
        {
            b.ToTable("certificate_documents");
            b.HasKey(d => d.Id);
            b.Ignore(d => d.HasContent);
            b.Property(d => d.ContentType).HasMaxLength(50);
            b.Property(d => d.State).HasConversion<string>().HasMaxLength(20);
            b.Property(d => d.RejectionReason).HasConversion<string>().HasMaxLength(20);
            b.HasIndex(d => d.AbsenceId);
            b.HasIndex(d => d.EmployeeId);
            b.HasIndex(d => d.UploadedBy);
            b.HasIndex(d => d.StorageId).IsUnique();
        });
        modelBuilder.Entity<CertificateSettingsRecord>(b =>
        {
            b.ToTable("certificate_settings");
            b.HasKey(s => s.Id);
            b.Property(s => s.Id).ValueGeneratedNever();
        });
        modelBuilder.Entity<StoreSettingsRecord>(b =>
        {
            b.ToTable("store_settings");
            b.HasKey(s => s.StoreId);
            b.Property(s => s.StoreId).ValueGeneratedNever();
        });
        modelBuilder.Entity<MainStoreCopyRecord>(b =>
        {
            b.ToTable("main_store_copies");
            b.HasKey(m => m.EmployeeId);
            b.Property(m => m.EmployeeId).ValueGeneratedNever();
        });
        modelBuilder.Entity<CertificateReminderSettingsRecord>(b =>
        {
            b.ToTable("certificate_reminder_settings");
            b.HasKey(s => s.Id);
            b.Property(s => s.Id).ValueGeneratedNever();
        });
        modelBuilder.Entity<CertificateAccessEntry>(b =>
        {
            b.ToTable("certificate_access_log");
            b.HasKey(e => e.Id);
            b.Property(e => e.Action).HasConversion<string>().HasMaxLength(30);
            b.HasIndex(e => e.At);
            b.HasIndex(e => e.EmployeeId);
            b.HasIndex(e => e.AbsenceId);
        });
    }
}
