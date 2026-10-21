using Microsoft.EntityFrameworkCore;
using Shared.Authorization;
using Shared.Messaging;

namespace Notifications.Infrastructure;

/// <summary>Own schema <c>notifications</c>; no cross-schema joins.</summary>
public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options)
{
    public const string Schema = "notifications";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ConfigureMessaging();
        modelBuilder.ConfigurePermissionCache();
    }
}
