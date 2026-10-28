using Microsoft.EntityFrameworkCore;
using Notifications.Infrastructure.Delivery;
using Shared.Infrastructure.Authorization;
using Shared.Infrastructure.Messaging;

namespace Notifications.Infrastructure;

/// <summary>Own schema <c>notifications</c>; no cross-schema joins.</summary>
public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options)
{
    public const string Schema = "notifications";

    public DbSet<InAppNotificationRecord> InApp => Set<InAppNotificationRecord>();
    public DbSet<DeliveryRow> Deliveries => Set<DeliveryRow>();
    public DbSet<PreferenceRow> Preferences => Set<PreferenceRow>();
    public DbSet<EmployeeNameRecord> EmployeeNames => Set<EmployeeNameRecord>();
    public DbSet<TemplateOverrideRecord> TemplateOverrides => Set<TemplateOverrideRecord>();

    /// <summary>Provider settings shared by runtime and design time (migrations).</summary>
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string connectionString) => options
        .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", Schema))
        .UseSnakeCaseNamingConvention();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ConfigureMessaging();
        modelBuilder.ConfigurePermissionCache();

        modelBuilder.Entity<InAppNotificationRecord>(b =>
        {
            b.ToTable("in_app_notifications");
            b.HasKey(n => n.Id);
            b.HasIndex(n => new { n.NotificationId, n.EmployeeId }).IsUnique();
            b.HasIndex(n => new { n.EmployeeId, n.CreatedAt });
            b.Property(n => n.TemplateKey).HasMaxLength(200);
            b.Property(n => n.Title).HasMaxLength(200);
            b.Property(n => n.Body).HasMaxLength(2000);
        });
        modelBuilder.Entity<DeliveryRow>(b =>
        {
            b.ToTable("deliveries");
            b.HasKey(d => new { d.NotificationId, d.EmployeeId, d.Channel });
            b.HasIndex(d => d.At);
            b.Property(d => d.Channel).HasMaxLength(20);
            b.Property(d => d.Status).HasMaxLength(30);
        });
        modelBuilder.Entity<EmployeeNameRecord>(b =>
        {
            b.ToTable("employee_names");
            b.HasKey(n => n.EmployeeId);
            b.Property(n => n.EmployeeId).ValueGeneratedNever();
            b.Property(n => n.FirstName).HasMaxLength(200);
            b.Property(n => n.LastName).HasMaxLength(200);
        });
        modelBuilder.Entity<TemplateOverrideRecord>(b =>
        {
            b.ToTable("template_overrides");
            b.HasKey(o => o.TemplateKey);
            b.Property(o => o.TemplateKey).HasMaxLength(200);
            b.Property(o => o.InAppTitle).HasMaxLength(200);
            b.Property(o => o.InAppBody).HasMaxLength(2000);
            b.Property(o => o.PushTitle).HasMaxLength(200);
            b.Property(o => o.PushBody).HasMaxLength(2000);
            b.Property(o => o.EmailTitle).HasMaxLength(200);
            b.Property(o => o.EmailBody).HasMaxLength(2000);
        });
        modelBuilder.Entity<PreferenceRow>(b =>
        {
            b.ToTable("preferences");
            b.HasKey(p => new { p.EmployeeId, p.TemplateKey, p.Channel });
            b.Property(p => p.TemplateKey).HasMaxLength(200);
            b.Property(p => p.Channel).HasMaxLength(20);
        });
    }
}
