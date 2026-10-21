using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BcIntegration.Infrastructure;

/// <summary>Used by <c>dotnet ef migrations add</c> only; it never connects, so no real connection string is needed.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<BcIntegrationDbContext>
{
    public BcIntegrationDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<BcIntegrationDbContext>();
        BcIntegrationDbContext.Configure(options, "Host=localhost;Database=design_time");
        return new BcIntegrationDbContext(options.Options);
    }
}
