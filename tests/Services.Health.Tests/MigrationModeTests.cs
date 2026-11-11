using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shared.Infrastructure.Persistence;
using Shared.Testing;
using Workforce.Infrastructure;

namespace Services.Health.Tests;

/// <summary>The deployment step: <c>--migrate</c> applies the migrations and ends the program; a failure must not be swallowed.</summary>
public sealed class MigrationModeTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static WebApplicationFactory<Workforce.Api.ApiMarker> Factory(string connectionString) =>
        new WebApplicationFactory<Workforce.Api.ApiMarker>().WithWebHostBuilder(host =>
        {
            host.UseEnvironment("Testing");
            host.UseSetting($"ConnectionStrings:{InfrastructureServiceCollectionExtensions.ConnectionName}", connectionString);
        });

    [Fact]
    public async Task Migrate_mode_applies_all_migrations_and_tells_the_program_to_end()
    {
        await using var factory = Factory(await postgres.CreateDatabaseAsync());
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkforceDbContext>();
        // The test host has migrated at start (Testing); take everything away again so that the deployment step has to do the work.
        await db.Database.ExecuteSqlRawAsync("DROP SCHEMA IF EXISTS workforce CASCADE");

        var end = await DatabaseInitializer.MigrateOrExitAsync<WorkforceDbContext>(factory.Services, factory.Services.GetRequiredService<IHostEnvironment>(), [DatabaseInitializer.MigrateOnlyArgument]);

        end.ShouldBeTrue();
        (await db.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        (await db.Database.GetAppliedMigrationsAsync()).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Without_the_argument_the_program_continues()
    {
        await using var factory = Factory(await postgres.CreateDatabaseAsync());

        (await DatabaseInitializer.MigrateOrExitAsync<WorkforceDbContext>(factory.Services, factory.Services.GetRequiredService<IHostEnvironment>(), [])).ShouldBeFalse();
    }

    [Fact]
    public async Task A_failing_migration_step_ends_with_an_error_instead_of_starting_against_an_unknown_database()
    {
        const string unreachable = "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1;Pooling=false";
        await using var factory = Factory(unreachable);

        await Should.ThrowAsync<Exception>(() => DatabaseInitializer.MigrateOrExitAsync<WorkforceDbContext>(
            factory.Services, factory.Services.GetRequiredService<IHostEnvironment>(), [DatabaseInitializer.MigrateOnlyArgument]));
    }
}
