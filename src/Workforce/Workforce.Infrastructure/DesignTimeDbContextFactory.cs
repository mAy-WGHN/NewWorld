using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Workforce.Infrastructure;

/// <summary>Used by <c>dotnet ef migrations add</c> only; it never connects, so no real connection string is needed.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<WorkforceDbContext>
{
    public WorkforceDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<WorkforceDbContext>();
        WorkforceDbContext.Configure(options, "Host=localhost;Database=design_time");
        return new WorkforceDbContext(options.Options);
    }
}
