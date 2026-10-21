using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Access.Infrastructure;

/// <summary>Used by <c>dotnet ef migrations add</c> only; it never connects, so no real connection string is needed.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AccessDbContext>
{
    public AccessDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AccessDbContext>();
        AccessDbContext.Configure(options, "Host=localhost;Database=design_time");
        return new AccessDbContext(options.Options);
    }
}
