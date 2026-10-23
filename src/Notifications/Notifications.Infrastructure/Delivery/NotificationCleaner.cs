using Microsoft.EntityFrameworkCore;

namespace Notifications.Infrastructure.Delivery;

public sealed class NotificationOptions
{
    public const string ConfigurationSection = "Notifications";

    /// <summary>How long in-app notifications (and their delivery records) are kept (plan: 90 days, configurable).</summary>
    public TimeSpan InAppRetention { get; set; } = TimeSpan.FromDays(90);

    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Folder with the template files; default: <c>Templates</c> next to the application.</summary>
    public string? TemplatesPath { get; set; }
}

/// <summary>Deletes in-app notifications and delivery records older than the retention (by creation time).</summary>
public sealed class NotificationCleaner(NotificationsDbContext db, TimeProvider time, NotificationOptions options)
{
    /// <returns>Number of deleted rows.</returns>
    public async Task<int> CleanAsync(CancellationToken cancellationToken)
    {
        var cutoff = time.GetUtcNow() - options.InAppRetention;
        var notifications = await db.InApp.Where(n => n.CreatedAt < cutoff).ExecuteDeleteAsync(cancellationToken);
        var deliveries = await db.Deliveries.Where(d => d.At < cutoff).ExecuteDeleteAsync(cancellationToken);
        return notifications + deliveries;
    }
}
