using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Portal.Infrastructure;

/// <summary>Used by <c>dotnet ef migrations add</c> only; it never connects, so no real connection string is needed.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<PortalDbContext>
{
    public PortalDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PortalDbContext>();
        PortalDbContext.Configure(options, "Host=localhost;Database=design_time");
        return new PortalDbContext(options.Options);
    }
}
