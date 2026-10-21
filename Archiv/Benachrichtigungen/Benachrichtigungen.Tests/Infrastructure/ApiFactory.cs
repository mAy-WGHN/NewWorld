using Benachrichtigungen.Data;
using Benachrichtigungen.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;

namespace Benachrichtigungen.Tests.Infrastructure;

/// <summary>
/// Startet den Dienst gegen ein frisches Postgres (Testcontainers) mit Fakes für Push-Versand, Empfängerermittlung (Rollen-Rechte) und Uhr.
/// Eine Instanz für alle Tests (Collection "Api"); Tests legen ihre Daten mit zufälligen IDs an und verlassen sich nie auf leere Tabellen.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string VapidPublicKey = "test-vapid-public-key";
    public const string JwtKey = "integration-test-key-0123456789abcdef0123456789";
    public const string Source = "portal";
    public const string SourceKey = "portal-source-key-0123456789abcdef0123456789";
    public const string FrontendBaseUrl = "https://frontend.test";

    // 2026-10-14 ist ein Mittwoch (mittags UTC, also gleicher Tag in Berlin); 2026-10-10 ein Samstag.
    public static readonly DateTimeOffset Wednesday = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset Saturday = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

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

    public FakePushSender PushSender { get; } = new();
    public FakeRecipientDirectory Recipients { get; } = new();
    public SettableClock Clock { get; } = new(Wednesday);

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Umgebungsvariablen statt UseSetting: Program liest die Konfiguration schon beim Bauen des Builders.
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("Jwt__Key", JwtKey);
        Environment.SetEnvironmentVariable("Vapid__PublicKey", VapidPublicKey);
        Environment.SetEnvironmentVariable("Vapid__PrivateKey", "test-vapid-private-key");
        Environment.SetEnvironmentVariable("Vapid__Subject", "mailto:test@example.com");
        Environment.SetEnvironmentVariable("Frontend__BaseUrl", FrontendBaseUrl);
        Environment.SetEnvironmentVariable("Cors__AllowedOriginPattern", @"^https?://(localhost|127\.0\.0\.1)(:[0-9]+)?$");
        Environment.SetEnvironmentVariable($"Sources__Keys__{Source}", SourceKey);

        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Database.MigrateAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IPushNotificationSender>();
            services.AddSingleton<IPushNotificationSender>(PushSender);
            services.RemoveAll<IRecipientDirectory>();
            services.AddSingleton<IRecipientDirectory>(Recipients);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>Führt Hilfsarbeiten direkt gegen die DB aus.</summary>
    public async Task<T> WithDbAsync<T>(Func<NotificationsDbContext, IServiceProvider, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<NotificationsDbContext>(), scope.ServiceProvider);
    }

    public Task WithDbAsync(Func<NotificationsDbContext, IServiceProvider, Task> action) =>
        WithDbAsync<object?>(async (db, sp) => { await action(db, sp); return null; });
}

[CollectionDefinition("Api")]
public class ApiCollection : ICollectionFixture<ApiFactory>;

/// <summary>Eine Uhr, die man beliebig setzen kann (auch zurück): Jeder Test beginnt am Mittwoch und stellt sie bei Bedarf um.</summary>
public sealed class SettableClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public void SetUtcNow(DateTimeOffset value) => _now = value;

    public override DateTimeOffset GetUtcNow() => _now;
}