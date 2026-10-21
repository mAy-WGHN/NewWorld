using System.Text.Json;
using Benachrichtigungen.Domain.Entities;
using Benachrichtigungen.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Benachrichtigungen.Data;

/// <summary>
/// Datenbank des Benachrichtigungs-Dienstes: Push-Abos, die Benachrichtigungs-Einstellungen der User, die (noch nicht ausgewerteten) Regeln und die
/// verarbeiteten Ereignisse. User und Rollen gehören dem Dienst "Rollen-Rechte"; hier stehen nur ihre IDs. Spaltennamen in snake_case.
/// </summary>
public class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options)
{
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();
    public DbSet<UserNotificationSettings> UserNotificationSettings => Set<UserNotificationSettings>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>()
            .HaveColumnType("timestamp(0) without time zone")
            .HaveConversion<UnspecifiedDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>()
            .HaveColumnType("timestamp(0) without time zone")
            .HaveConversion<NullableUnspecifiedDateTimeConverter>();

        // Enums liegen als VARCHAR(255) mit Kleinbuchstaben-Wert in der DB.
        configurationBuilder.Properties<ReportSeverity>().HaveMaxLength(255).HaveConversion<LowerCaseEnumConverter<ReportSeverity>>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PushSubscription>(e =>
        {
            e.ToTable("push_subscriptions");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Endpoint).HasColumnType("text");
            e.Property(x => x.PublicKey).HasMaxLength(255);
            e.Property(x => x.AuthToken).HasMaxLength(255);
            e.HasIndex(x => x.Endpoint).IsUnique().HasDatabaseName("uniq_push_subscription_endpoint");
            e.HasIndex(x => x.UserId);
        });

        modelBuilder.Entity<UserNotificationSettings>(e =>
        {
            e.ToTable("user_notification_settings");
            e.HasKey(x => x.UserId);
            e.Property(x => x.UserId).ValueGeneratedNever();
            e.Property(x => x.NotificationWeekdays)
                .HasColumnType("json")
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<List<int>>(v, (JsonSerializerOptions?)null) ?? new List<int>(),
                    new ValueComparer<List<int>>(
                        (a, b) => a!.SequenceEqual(b!),
                        v => v.Aggregate(0, (h, i) => HashCode.Combine(h, i)),
                        v => v.ToList()));
        });

        modelBuilder.Entity<ProcessedEvent>(e =>
        {
            e.ToTable("processed_event");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Type).HasMaxLength(100);
            e.Property(x => x.Source).HasMaxLength(50);
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

    private void TouchUpdatedAt()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries().Where(e => e.State == EntityState.Modified))
        {
            switch (entry.Entity)
            {
                case UserNotificationSettings s: s.UpdatedAt = now; break;
            }
        }
    }
}
