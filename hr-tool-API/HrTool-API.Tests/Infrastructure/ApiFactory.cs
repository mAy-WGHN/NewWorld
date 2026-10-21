using HrTool_API.Authentication;
using HrTool_API.Data;
using HrTool_API.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;

namespace HrTool_API.Tests.Infrastructure;

/// <summary>
/// Startet die komplette API gegen ein frisches Postgres (Testcontainers) mit ersetzten Nachbardiensten (Rollen-Rechte). Eine Instanz für alle Tests
/// (Collection "Api"); Tests legen ihre Daten mit zufälligen Werten an und verlassen sich nie auf leere Tabellen.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string JwtKey = "integration-test-key-0123456789abcdef0123456789";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    static ApiFactory()
    {
        // Docker Desktop (Windows) stellt den Engine-Pipe als "dockerDesktopLinuxEngine" bereit; Testcontainers
        // probiert standardmäßig "docker_engine". Ohne gesetztes DOCKER_HOST den vorhandenen Pipe automatisch nutzen.
        if (Environment.GetEnvironmentVariable("DOCKER_HOST") is null && OperatingSystem.IsWindows() &&
            Directory.EnumerateFiles(@"\\.\pipe\").Any(p => p.EndsWith("dockerDesktopLinuxEngine", StringComparison.Ordinal)))
        {
            Environment.SetEnvironmentVariable("DOCKER_HOST", "npipe://./pipe/dockerDesktopLinuxEngine");
        }
    }

    public FakeAccessDirectory Access { get; } = new();
    public FakeLocationDirectory Locations { get; } = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Umgebungsvariablen statt UseSetting: Program liest die Konfiguration schon beim Bauen des Builders.
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("Jwt__Key", JwtKey);
        Environment.SetEnvironmentVariable("ReferenceData__MaintenanceEnabled", "false"); // Seeder und Filialabgleich testen die Tests direkt
        Environment.SetEnvironmentVariable("Cors__AllowedOriginPattern", @"^https?://(localhost|127\.0\.0\.1)(:[0-9]+)?$");

        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<HrToolDbContext>().Database.MigrateAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAccessDirectory>();
            services.AddSingleton<IAccessDirectory>(Access);
            services.RemoveAll<ILocationDirectory>();
            services.AddSingleton<ILocationDirectory>(Locations);

            services.AddControllers().AddApplicationPart(typeof(ProbeController).Assembly);
        });
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>Führt Hilfsarbeiten direkt gegen die DB aus (am HTTP-Layer vorbei). Ohne gesetzten Scope = uneingeschränkt.</summary>
    public async Task<T> WithDbAsync<T>(Func<HrToolDbContext, IServiceProvider, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<HrToolDbContext>(), scope.ServiceProvider);
    }

    public Task WithDbAsync(Func<HrToolDbContext, IServiceProvider, Task> action) =>
        WithDbAsync<object?>(async (db, sp) => { await action(db, sp); return null; });
}

[CollectionDefinition("Api")]
public class ApiCollection : ICollectionFixture<ApiFactory>;
