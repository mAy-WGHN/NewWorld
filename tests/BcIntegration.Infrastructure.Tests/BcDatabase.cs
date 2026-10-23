using BcIntegration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shared.Testing;

namespace BcIntegration.Infrastructure.Tests;

internal static class BcDatabase
{
    /// <summary>Fresh database with the real migrations applied.</summary>
    public static async Task<string> CreateAsync(PostgresFixture postgres)
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var db = Open(connectionString);
        await db.Database.MigrateAsync();
        return connectionString;
    }

    public static BcIntegrationDbContext Open(string connectionString)
    {
        var options = new DbContextOptionsBuilder<BcIntegrationDbContext>();
        BcIntegrationDbContext.Configure(options, connectionString);
        return new BcIntegrationDbContext(options.Options);
    }
}
