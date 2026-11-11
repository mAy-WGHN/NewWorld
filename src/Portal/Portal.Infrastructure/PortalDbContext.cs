using Shared.Infrastructure.Audit;
using Microsoft.EntityFrameworkCore;
using Portal.Core.Reports;
using Portal.Infrastructure.Faqs;
using Portal.Infrastructure.Logbook;
using Portal.Infrastructure.Messaging;
using Portal.Infrastructure.Reports;
using Shared.Infrastructure.Authorization;
using Shared.Infrastructure.Messaging;

namespace Portal.Infrastructure;

/// <summary>Own schema <c>portal</c>; no cross-schema joins.</summary>
public sealed class PortalDbContext(DbContextOptions<PortalDbContext> options) : DbContext(options)
{
    public const string Schema = "portal";

    public DbSet<PersonRecord> People => Set<PersonRecord>();
    public DbSet<StoreRecord> Stores => Set<StoreRecord>();
    public DbSet<MainStoreCopyRecord> MainStoreCopies => Set<MainStoreCopyRecord>();
    public DbSet<FaqRecord> Faqs => Set<FaqRecord>();
    public DbSet<FaqImageRecord> FaqImages => Set<FaqImageRecord>();
    public DbSet<FaqAttachmentRecord> FaqAttachments => Set<FaqAttachmentRecord>();
    public DbSet<ReportRecord> Reports => Set<ReportRecord>();
    public DbSet<ReportLocationRecord> ReportLocations => Set<ReportLocationRecord>();
    public DbSet<ReportRecipientRecord> ReportRecipients => Set<ReportRecipientRecord>();
    public DbSet<LogbookDistrictRecord> LogbookDistricts => Set<LogbookDistrictRecord>();
    public DbSet<LogbookDistrictLeaderRecord> LogbookDistrictLeaders => Set<LogbookDistrictLeaderRecord>();
    public DbSet<LogbookRegionRecord> LogbookRegions => Set<LogbookRegionRecord>();
    public DbSet<LogbookRegionLeaderRecord> LogbookRegionLeaders => Set<LogbookRegionLeaderRecord>();
    public DbSet<LogbookStoreAssignmentRecord> LogbookStoreAssignments => Set<LogbookStoreAssignmentRecord>();
    public DbSet<LogbookTemplateRecord> LogbookTemplates => Set<LogbookTemplateRecord>();
    public DbSet<LogbookReportRecord> LogbookReports => Set<LogbookReportRecord>();

    /// <summary>Provider settings shared by runtime and design time (migrations).</summary>
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string connectionString) => options
        .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", Schema))
        .UseSnakeCaseNamingConvention();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ConfigureMessaging();
        modelBuilder.ConfigureAudit();
        modelBuilder.ConfigurePermissionCache();
        modelBuilder.Entity<PersonRecord>(b =>
        {
            b.ToTable("people");
            b.HasKey(p => p.EmployeeId);
            b.HasIndex(p => p.HomeStoreId);
        });
        modelBuilder.Entity<MainStoreCopyRecord>(b =>
        {
            b.ToTable("main_store_copies");
            b.HasKey(m => m.EmployeeId);
            b.Property(m => m.EmployeeId).ValueGeneratedNever();
        });
        modelBuilder.Entity<StoreRecord>(b =>
        {
            b.ToTable("stores");
            b.HasKey(s => s.StoreId);
        });
        modelBuilder.Entity<ReportRecord>(b =>
        {
            b.ToTable("reports");
            b.HasKey(x => x.Id);
            b.Property(x => x.Status).HasConversion(new LowerCaseEnumConverter<ReportStatus>()).HasMaxLength(255);
            b.Property(x => x.Source).HasConversion(new LowerCaseEnumConverter<ReportSource>()).HasMaxLength(255);
            b.Property(x => x.Category).HasConversion(new LowerCaseEnumConverter<ReportCategory>()).HasMaxLength(255);
            b.Property(x => x.Severity).HasConversion(new LowerCaseEnumConverter<ReportSeverity>()).HasMaxLength(255);
            b.Property(x => x.Device).HasMaxLength(120);
            b.Property(x => x.NoticedAt).HasColumnType("timestamp without time zone").HasConversion(new NullableUnspecifiedDateTimeConverter());
            b.Property(x => x.CreatedAt).HasColumnType("timestamp without time zone").HasConversion(new UnspecifiedDateTimeConverter());
            b.Property(x => x.UpdatedAt).HasColumnType("timestamp without time zone").HasConversion(new UnspecifiedDateTimeConverter());
            b.HasIndex(x => x.CreatedAt);
            b.HasIndex(x => x.ParentReportId);
            b.HasMany(x => x.Locations).WithOne().HasForeignKey(l => l.ReportId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ReportLocationRecord>(b =>
        {
            b.ToTable("report_locations");
            b.HasKey(x => new { x.ReportId, x.LocationId });
            b.HasIndex(x => x.LocationId);
        });
        modelBuilder.Entity<ReportRecipientRecord>(b =>
        {
            b.ToTable("report_recipients");
            b.HasKey(x => new { x.Category, x.EmployeeId });
            b.Property(x => x.Category).HasConversion(new LowerCaseEnumConverter<ReportCategory>()).HasMaxLength(255);
        });
        modelBuilder.Entity<LogbookDistrictRecord>(b =>
        {
            b.ToTable("logbook_districts");
            b.HasKey(d => d.Id);
            b.Property(d => d.Id).ValueGeneratedNever();
            b.Property(d => d.Name).HasMaxLength(120);
        });
        modelBuilder.Entity<LogbookDistrictLeaderRecord>(b =>
        {
            b.ToTable("logbook_district_leaders");
            b.HasKey(l => new { l.DistrictId, l.EmployeeId });
            b.HasIndex(l => l.EmployeeId);
            b.HasOne<LogbookDistrictRecord>().WithMany().HasForeignKey(l => l.DistrictId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<LogbookRegionRecord>(b =>
        {
            b.ToTable("logbook_regions");
            b.HasKey(r => r.Id);
            b.Property(r => r.Id).ValueGeneratedNever();
            b.Property(r => r.Name).HasMaxLength(120);
            b.HasIndex(r => r.DistrictId);
            b.HasOne<LogbookDistrictRecord>().WithMany().HasForeignKey(r => r.DistrictId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<LogbookRegionLeaderRecord>(b =>
        {
            b.ToTable("logbook_region_leaders");
            b.HasKey(l => new { l.RegionId, l.EmployeeId });
            b.HasIndex(l => l.EmployeeId);
            b.HasOne<LogbookRegionRecord>().WithMany().HasForeignKey(l => l.RegionId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<LogbookStoreAssignmentRecord>(b =>
        {
            b.ToTable("logbook_store_assignments");
            b.HasKey(s => s.StoreId);
            b.Property(s => s.StoreId).ValueGeneratedNever();
            b.HasIndex(s => s.DistrictId);
            b.HasIndex(s => s.RegionId);
        });
        modelBuilder.Entity<LogbookTemplateRecord>(b =>
        {
            b.ToTable("logbook_templates");
            b.HasKey(t => t.Version);
            b.Property(t => t.Version).ValueGeneratedNever();
            b.Property(t => t.SectionsJson).HasColumnType("jsonb");
        });
        modelBuilder.Entity<LogbookReportRecord>(b =>
        {
            b.ToTable("logbook_reports");
            b.HasKey(r => r.Id);
            b.Property(r => r.Id).ValueGeneratedNever();
            b.Property(r => r.Phase).HasConversion<string>().HasMaxLength(20);
            b.Property(r => r.State).HasConversion<string>().HasMaxLength(20);
            b.Property(r => r.AnswersJson).HasColumnType("jsonb");
            b.HasIndex(r => new { r.StoreId, r.Week }).IsUnique();
            b.HasIndex(r => r.Week);
        });
        modelBuilder.Entity<FaqRecord>(b =>
        {
            b.ToTable("faqs");
            b.HasKey(f => f.Id);
            b.Property(f => f.Title).HasMaxLength(255);
            b.Property(f => f.ContentJson).HasColumnType("jsonb");
            b.HasIndex(f => new { f.Archived, f.SortOrder });
        });
        modelBuilder.Entity<FaqAttachmentRecord>(b =>
        {
            b.ToTable("faq_attachments");
            b.HasKey(a => a.Id);
            b.Property(a => a.FileName).HasMaxLength(255);
            b.Property(a => a.Extension).HasMaxLength(8);
            b.HasIndex(a => a.FaqId);
            // Deleting an entry deletes its attachments (the endpoint removes the files).
            b.HasOne<FaqRecord>().WithMany().HasForeignKey(a => a.FaqId).OnDelete(DeleteBehavior.Cascade);
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
