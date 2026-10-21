using System.Net;
using Microsoft.Extensions.DependencyInjection;

namespace AppHost.Tests;

/// <summary>Starts the whole landscape once per test class (PostgreSQL, Service Bus emulator, WireMock, five services).</summary>
public sealed class AppHostFixture : IAsyncLifetime
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);

    private DistributedApplication? _app;

    public DistributedApplication App => _app ?? throw new InvalidOperationException("AppHost not started.");

    public async Task InitializeAsync()
    {
        using var cts = new CancellationTokenSource(StartupTimeout);
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(cts.Token);
        _app = await builder.BuildAsync(cts.Token);
        await _app.StartAsync(cts.Token);
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }
}

/// <summary>
/// E2E: after the AppHost start every service answers 200 on /health/ready. Needs Docker; the first run pulls images.
/// </summary>
[Trait("Category", "E2E")]
public class AppHostHealthTests(AppHostFixture fixture) : IClassFixture<AppHostFixture>
{
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromMinutes(4);

    [Theory]
    [InlineData("bc-integration-api")]
    [InlineData("access-api")]
    [InlineData("notifications-api")]
    [InlineData("workforce-api")]
    [InlineData("portal-api")]
    public async Task Service_is_ready_after_apphost_start(string service)
    {
        using var cts = new CancellationTokenSource(ReadyTimeout);
        try
        {
            await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(service, cts.Token);
        }
        catch (DistributedApplicationException)
        {
            var logger = fixture.App.Services.GetRequiredService<ResourceLoggerService>();
            var lines = new List<string>();
            using var logCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try
            {
                foreach (var l in (await logger.GetAllAsync(service).ToListAsync(logCts.Token)).SelectMany(b => b))
                {
                    lines.Add(l.Content);
                }
            }
            catch (OperationCanceledException)
            {
                // collected what was available
            }

            var snapshot = fixture.App.Services.GetRequiredService<ResourceNotificationService>().TryGetCurrentState(service, out var ev)
                ? $"state={ev!.Snapshot.State?.Text}; exit={ev.Snapshot.ExitCode}; envNames={string.Join(",", ev.Snapshot.EnvironmentVariables.Select(v => v.Name).Where(n => n.StartsWith("ConnectionStrings", StringComparison.Ordinal) || n.StartsWith("OTEL_", StringComparison.Ordinal) || n.StartsWith("ASPNETCORE", StringComparison.Ordinal) || n.StartsWith("DOTNET", StringComparison.Ordinal)))}"
                : "no state";
            throw new InvalidOperationException($"{service} failed to start. {snapshot}. Resource log:{Environment.NewLine}{string.Join(Environment.NewLine, lines)}");
        }

        using var client = fixture.App.CreateHttpClient(service);
        using var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), cts.Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
