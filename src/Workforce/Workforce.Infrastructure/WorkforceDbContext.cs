using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure.Approvals;
using Workforce.Core.Absences;
using Workforce.Core.Certificates;
using Workforce.Core.Unavailability;
using Workforce.Core.WorkingTime;
using Workforce.Infrastructure.WorkingTime;
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
    public DbSet<TimeBooking> TimeBookings => Set<TimeBooking>();
    public DbSet<MonthClose> MonthCloses => Set<MonthClose>();
    public DbSet<WorkingTimeRulesRecord> WorkingTimeRules => Set<WorkingTimeRulesRecord>();
    public DbSet<MonthlyThresholdRecord> MonthlyThresholds => Set<MonthlyThresholdRecord>();
    public DbSet<UnavailabilityKind> UnavailabilityKinds => Set<UnavailabilityKind>();
    public DbSet<UnavailabilityEntry> UnavailabilityEntries => Set<UnavailabilityEntry>();
    public DbSet<EntitlementRecord> Entitlements => Set<EntitlementRecord>();
    public DbSet<ExtraVacationRecord> ExtraVacation => Set<ExtraVacationRecord>();
    public DbSet<VacationRulesRecord> VacationRules => Set<VacationRulesRecord>();
    public DbSet<EntitlementChangeRecord> EntitlementChanges => Set<EntitlementChangeRecord>();
    public DbSet<PersonRecord> People => Set<PersonRecord>();
    public DbSet<StoreRecord> Stores => Set<StoreRecord>();
    public DbSet<ApproverCopyRecord> ApproverCopies => Set<ApproverCopyRecord>();
    public DbSet<StoreSettingsRecord> StoreSettings => Set<StoreSettingsRecord>();
    public DbSet<Workforce.Core.Employees.EmployeeCard> EmployeeCards => Set<Workforce.Core.Employees.EmployeeCard>();
    public DbSet<Workforce.Core.Employees.EmployeeChangeRequest> EmployeeChangeRequests => Set<Workforce.Core.Employees.EmployeeChangeRequest>();
    public DbSet<Workforce.Core.Employees.ReferenceEntry> ReferenceEntries => Set<Workforce.Core.Employees.ReferenceEntry>();
    public DbSet<Employees.PersonnelNumberSeriesRecord> PersonnelNumberSeries => Set<Employees.PersonnelNumberSeriesRecord>();
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
        // Arbeitszeitkonto: bookings, month closes, the rules HR sets for everybody and the monthly threshold per employment relationship.
        modelBuilder.Entity<TimeBooking>(b =>
        {
            b.ToTable("time_bookings");
            b.HasKey(t => t.Id);
            b.Property(t => t.Id).ValueGeneratedNever();
            b.Property(t => t.Kind).HasConversion<string>().HasMaxLength(20);
            b.Property(t => t.Hours).HasPrecision(8, 2);
            b.HasIndex(t => new { t.EmployeeId, t.Date });
        });
        modelBuilder.Entity<MonthClose>(b =>
        {
            b.ToTable("month_closes");
            b.HasKey(c => c.Id);
            b.Property(c => c.Id).ValueGeneratedNever();
            b.Property(c => c.WorkedHours).HasPrecision(8, 2);
            b.Property(c => c.ThresholdHours).HasPrecision(8, 2);
            b.Property(c => c.OvertimeHours).HasPrecision(8, 2);
            b.Property(c => c.MinusHours).HasPrecision(8, 2).HasDefaultValue(0m);
            b.HasIndex(c => new { c.EmployeeId, c.Year, c.Month }).IsUnique();
        });
        modelBuilder.Entity<WorkingTimeRulesRecord>(b =>
        {
            b.ToTable("working_time_rules");
            b.HasKey(r => r.Id);
            b.Property(r => r.Id).ValueGeneratedNever();
            b.Property(r => r.PayoutThresholdHours).HasPrecision(8, 2);
            b.Property(r => r.BookMinusHours).HasDefaultValue(false);
        });
        modelBuilder.Entity<MonthlyThresholdRecord>(b =>
        {
            b.ToTable("monthly_thresholds");
            b.HasKey(t => t.RelationshipId);
            b.Property(t => t.RelationshipId).ValueGeneratedNever();
            b.Property(t => t.Hours).HasPrecision(8, 2);
        });

        // Nicht-Verfügbarkeit: the kinds (BC list, maintained by HR, seeded with the 24 of BC) and the entries (HR, and KU days from sick notes).
        modelBuilder.Entity<UnavailabilityKind>(b =>
        {
            b.ToTable("unavailability_kinds");
            b.HasKey(k => k.Id);
            b.Property(k => k.Id).ValueGeneratedNever();
            b.Property(k => k.Code).HasMaxLength(40);
            b.Property(k => k.Description).HasMaxLength(100);
            b.Property(k => k.ScheduleSymbol).HasMaxLength(10);
            b.Property(k => k.WorkCode).HasMaxLength(20);
            b.HasIndex(k => k.Code).IsUnique();
            b.HasData(UnavailabilityKindSeed.All.Select(s => s.ToKind()));
        });
        modelBuilder.Entity<UnavailabilityEntry>(b =>
        {
            b.ToTable("unavailability_entries");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedNever();
            b.Ignore(e => e.Range);
            b.Property(e => e.Source).HasConversion<string>().HasMaxLength(20);
            b.HasIndex(e => new { e.EmployeeId, e.From });
            b.HasIndex(e => e.SourceAbsenceId);
        });
        modelBuilder.Entity<EntitlementChangeRecord>(b =>
        {
            b.ToTable("entitlement_changes");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedNever();
            b.Property(e => e.AnnualDays).HasPrecision(6, 2);
            b.HasIndex(e => e.EmployeeId);
        });
        modelBuilder.Entity<VacationRulesRecord>(b =>
        {
            b.ToTable("vacation_rules");
            b.HasKey(v => v.Id);
            b.Property(v => v.Id).ValueGeneratedNever();
            b.Property(v => v.WaitingMode).HasConversion<string>().HasMaxLength(20).HasDefaultValue(Workforce.Core.Absences.WaitingPeriodMode.Accrued);
            b.Property(v => v.Rounding).HasConversion<string>().HasMaxLength(20).HasDefaultValue(Workforce.Core.Absences.ProRataRounding.Up);
            b.Property(v => v.RoundingStep).HasPrecision(3, 1).HasDefaultValue(0.5m);
        });
        modelBuilder.Entity<ExtraVacationRecord>(b =>
        {
            b.ToTable("extra_vacation_days");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedNever();
            b.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
            b.Property(e => e.Days).HasPrecision(5, 1);
            b.HasIndex(e => e.EmployeeId);
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
        modelBuilder.Entity<Workforce.Core.Employees.EmployeeCard>(b =>
        {
            b.ToTable("employee_cards");
            b.HasKey(c => c.EmployeeId);
            b.Property(c => c.EmployeeId).ValueGeneratedNever();
            b.Property(c => c.PersonnelNumber).HasMaxLength(20);
            b.HasIndex(c => c.PersonnelNumber).IsUnique();
            b.Property(c => c.FirstName).HasMaxLength(100);
            b.Property(c => c.LastName).HasMaxLength(100);
            b.Property(c => c.ReceiptName).HasMaxLength(100);
            b.Property(c => c.Gender).HasConversion<string>().HasMaxLength(20);
            b.Property(c => c.State).HasConversion<string>().HasMaxLength(20);
            b.Property(c => c.Street).HasMaxLength(200);
            b.Property(c => c.PostalCode).HasMaxLength(10);
            b.Property(c => c.City).HasMaxLength(100);
            b.Property(c => c.PrivatePhone).HasMaxLength(30);
            b.Property(c => c.PrivateEmail).HasMaxLength(200);
            b.Property(c => c.BusinessEmail).HasMaxLength(200);
            b.Property(c => c.Note).HasMaxLength(1000);
            b.Ignore(c => c.FullName);
            b.HasIndex(c => c.MainStoreId);
        });
        modelBuilder.Entity<Workforce.Core.Employees.EmployeeChangeRequest>(b =>
        {
            b.ToTable("employee_change_requests");
            b.HasKey(c => c.Id);
            b.Property(c => c.Id).ValueGeneratedNever();
            b.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(c => c.Street).HasMaxLength(200);
            b.Property(c => c.PostalCode).HasMaxLength(10);
            b.Property(c => c.City).HasMaxLength(100);
            b.Property(c => c.PrivatePhone).HasMaxLength(30);
            b.Property(c => c.PrivateEmail).HasMaxLength(200);
            b.Property(c => c.Message).HasMaxLength(500);
            b.Property(c => c.RejectionReason).HasMaxLength(200);
            b.HasIndex(c => c.EmployeeId);
            b.HasIndex(c => c.Status);
        });
        modelBuilder.Entity<Workforce.Core.Employees.ReferenceEntry>(b =>
        {
            b.ToTable("reference_entries");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedNever();
            b.Property(e => e.Kind).HasConversion<string>().HasMaxLength(30);
            b.Property(e => e.Code).HasMaxLength(30);
            b.Property(e => e.Name).HasMaxLength(100);
            b.HasIndex(e => new { e.Kind, e.Code }).IsUnique();
        });
        modelBuilder.Entity<Employees.PersonnelNumberSeriesRecord>(b =>
        {
            b.ToTable("personnel_number_series");
            b.HasKey(s => s.Id);
            b.Property(s => s.Id).ValueGeneratedNever();
            b.Property(s => s.StartNumber).HasDefaultValue(1001L);
            b.Property(s => s.EndNumber).HasDefaultValue(99999L);
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
