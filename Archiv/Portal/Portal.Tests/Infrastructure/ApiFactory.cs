using Portal.Authentication;
using Portal.Data;
using Portal.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;

namespace Portal.Tests.Infrastructure;

/// <summary>
/// Startet die komplette API gegen ein frisches Postgres (Testcontainers). Eine Instanz für alle Tests
/// (Collection "Api"); Tests legen ihre Daten mit zufälligen Namen an und verlassen sich nie auf leere Tabellen.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string JwtKey = "integration-test-key-0123456789abcdef0123456789";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly string _uploadDir = Path.Combine(Path.GetTempPath(), "irs-api-tests", Guid.NewGuid().ToString("N"));

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
    public FakeReportEvents Events { get; } = new();
    public string UploadDir => _uploadDir;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Umgebungsvariablen statt UseSetting: Program liest die Konfiguration schon beim Bauen des Builders.
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("Jwt__Key", JwtKey);
        Environment.SetEnvironmentVariable("Uploads__FaqPath", _uploadDir);
        Environment.SetEnvironmentVariable("Cors__AllowedOriginPattern", @"^https?://(localhost|127\.0\.0\.1)(:[0-9]+)?$");

        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IrsDbContext>().Database.MigrateAsync();
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
            services.RemoveAll<IReportEvents>();
            services.AddSingleton<IReportEvents>(Events);
        });
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
        if (Directory.Exists(_uploadDir)) Directory.Delete(_uploadDir, recursive: true);
    }

    /// <summary>Führt Hilfsarbeiten direkt gegen die DB aus (am HTTP-Layer und den Rate-Limits vorbei).</summary>
    public async Task<T> WithDbAsync<T>(Func<IrsDbContext, IServiceProvider, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IrsDbContext>(), scope.ServiceProvider);
    }

    public Task WithDbAsync(Func<IrsDbContext, IServiceProvider, Task> action) =>
        WithDbAsync<object?>(async (db, sp) => { await action(db, sp); return null; });
}

[CollectionDefinition("Api")]
public class ApiCollection : ICollectionFixture<ApiFactory>;
