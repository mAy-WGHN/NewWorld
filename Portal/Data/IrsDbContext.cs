using Portal.Authentication;
using Portal.Domain.Entities;
using Portal.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Portal.Data;

public class IrsDbContext(DbContextOptions<IrsDbContext> options, ScopeContext? scopeContext = null) : DbContext(options)
{
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<ReportLocation> ReportLocations => Set<ReportLocation>();
    public DbSet<Faq> Faqs => Set<Faq>();
    public DbSet<FaqImage> FaqImages => Set<FaqImage>();

    // Scope der laufenden Anfrage (von der Rechteprüfung gesetzt), für den Abfragefilter auf Meldungen. Ohne Anfrage-Kontext
    // (anonyme Meldung, Tests) ist nichts gesetzt = uneingeschränkt. Bewusst Bool/Arrays: EF parametrisiert sie je Abfrage.
    public bool ScopeAll => scopeContext?.Current.All ?? true;
    public Guid[] ScopeLocations => scopeContext?.Current.Locations.ToArray() ?? [];
    public Guid[] ScopeReports => scopeContext?.Current.Reports.ToArray() ?? [];

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>()
            .HaveColumnType("timestamp(0) without time zone")
            .HaveConversion<UnspecifiedDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>()
            .HaveColumnType("timestamp(0) without time zone")
            .HaveConversion<NullableUnspecifiedDateTimeConverter>();

        // Enums liegen im bestehenden Schema als VARCHAR(255) mit Kleinbuchstaben-Wert vor.
        configurationBuilder.Properties<ReportCategory>().HaveMaxLength(255).HaveConversion<LowerCaseEnumConverter<ReportCategory>>();
        configurationBuilder.Properties<ReportSeverity>().HaveMaxLength(255).HaveConversion<LowerCaseEnumConverter<ReportSeverity>>();
        configurationBuilder.Properties<ReportSource>().HaveMaxLength(255).HaveConversion<LowerCaseEnumConverter<ReportSource>>();
        configurationBuilder.Properties<ReportStatus>().HaveMaxLength(255).HaveConversion<LowerCaseEnumConverter<ReportStatus>>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Report>(e =>
        {
            e.ToTable("reports");
            // Scope: nur Meldungen, die zu einer Filiale oder einer freigegebenen Einzelmeldung im Scope der Anfrage gehören ("eine Filiale genügt").
            e.HasQueryFilter(r => ScopeAll || r.ReportLocations.Any(l => ScopeLocations.Contains(l.LocationId)) || ScopeReports.Contains(r.Id));
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Description).HasColumnType("text");
            e.Property(x => x.Device).HasMaxLength(120);
            e.Property(x => x.NoticedHow).HasColumnType("text");
            e.Property(x => x.PriorActivity).HasColumnType("text");
            e.Property(x => x.Comment).HasColumnType("text");

            e.HasOne(x => x.ParentReport)
                .WithMany(x => x.ChildReports)
                .HasForeignKey(x => x.ParentReportId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasMany(x => x.ReportLocations).WithOne().HasForeignKey(x => x.ReportId).OnDelete(DeleteBehavior.Cascade);
        });

        // Die Filialen gehören Rollen-Rechte; hier nur ihre IDs, bewusst ohne Fremdschlüssel. Die Verbindung selbst ist ungefiltert: Eine sichtbare Meldung
        // zeigt alle ihre Filialen (nicht nur die im Scope), wie bisher.
        modelBuilder.Entity<ReportLocation>(e =>
        {
            e.ToTable("report_location");
            e.HasKey(x => new { x.ReportId, x.LocationId });
            e.HasIndex(x => x.LocationId);
        });

        modelBuilder.Entity<Faq>(e =>
        {
            e.ToTable("faqs");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Title).HasMaxLength(255);
            e.Property(x => x.Description).HasColumnType("text");
        });

        modelBuilder.Entity<FaqImage>(e =>
        {
            e.ToTable("faq_images");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Extension).HasMaxLength(10);
            e.HasOne(x => x.Faq).WithMany().HasForeignKey(x => x.FaqId).OnDelete(DeleteBehavior.SetNull);
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        TouchUpdatedAt();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        TouchUpdatedAt();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    // Entspricht dem Doctrine-#[PreUpdate] auf Report.
    private void TouchUpdatedAt()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries().Where(e => e.State == EntityState.Modified))
        {
            if (entry.Entity is Report r) r.UpdatedAt = now;
        }
    }
}
