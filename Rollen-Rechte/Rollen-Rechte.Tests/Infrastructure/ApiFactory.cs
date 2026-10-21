using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rollen_Rechte.Data;
using Rollen_Rechte.Services;
using Testcontainers.PostgreSql;

namespace Rollen_Rechte.Tests.Infrastructure;

/// <summary>
/// Startet den Dienst gegen ein frisches Postgres (Testcontainers) mit einem Fake statt der BC-Anbindung. Eine Instanz für alle Tests
/// (Collection "Api"); Tests legen ihre Daten mit zufälligen Namen an und verlassen sich nie auf leere Tabellen.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AppKey = "portal";
    public const string AppApiKey = "portal-app-key-0123456789abcdef0123456789";

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

    public FakeBcGateway Bc { get; } = new();
    public static readonly string PinKey = Convert.ToBase64String(new byte[32].Select((_, i) => (byte)(i + 1)).ToArray());

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Umgebungsvariablen statt UseSetting: Program liest die Konfiguration schon beim Bauen des Builders.
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("Jwt__Key", "integration-test-key-0123456789abcdef0123456789");
        Environment.SetEnvironmentVariable("Auth__BcryptCost", "4"); // minimale Kosten, damit die Tests schnell sind
        Environment.SetEnvironmentVariable("Cors__AllowedOriginPattern", @"^https?://(localhost|127\.0\.0\.1)(:[0-9]+)?$");
        Environment.SetEnvironmentVariable("Auth__PinEncryptionKey", PinKey);
        Environment.SetEnvironmentVariable("BcGateway__BaseUrl", "http://bc-anbindung.test");
        Environment.SetEnvironmentVariable("BcGateway__ApiKey", "test");
        Environment.SetEnvironmentVariable("Locations__SyncOnStartup", "false");
        Environment.SetEnvironmentVariable($"Apps__Keys__{AppKey}", AppApiKey);

        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AccessDbContext>().Database.MigrateAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IBcGateway>();
            services.AddSingleton<IBcGateway>(Bc);
        });
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>Führt Hilfsarbeiten direkt gegen die DB aus (am HTTP-Layer und den Rate-Limits vorbei).</summary>
    public async Task<T> WithDbAsync<T>(Func<AccessDbContext, IServiceProvider, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AccessDbContext>(), scope.ServiceProvider);
    }

    public Task WithDbAsync(Func<AccessDbContext, IServiceProvider, Task> action) =>
        WithDbAsync<object?>(async (db, sp) => { await action(db, sp); return null; });
}

[CollectionDefinition("Api")]
public class ApiCollection : ICollectionFixture<ApiFactory>;
