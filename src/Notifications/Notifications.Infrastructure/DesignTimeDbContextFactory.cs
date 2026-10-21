using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Notifications.Infrastructure;

/// <summary>Used by <c>dotnet ef migrations add</c> only; it never connects, so no real connection string is needed.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<NotificationsDbContext>
{
    public NotificationsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<NotificationsDbContext>();
        NotificationsDbContext.Configure(options, "Host=localhost;Database=design_time");
        return new NotificationsDbContext(options.Options);
    }
}
