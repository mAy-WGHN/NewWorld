using System.Net;
using Microsoft.Extensions.DependencyInjection;

namespace AppHost.Tests;

/// <summary>All E2E tests share one landscape (PostgreSQL, Service Bus emulator, WireMock, five services).</summary>
[CollectionDefinition(Name)]
public sealed class AppHostGroup : ICollectionFixture<AppHostFixture>
{
    public const string Name = "AppHost";
}

/// <summary>Starts the whole landscape once.</summary>
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
[Collection(AppHostGroup.Name)]
public class AppHostHealthTests(AppHostFixture fixture)
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
            throw new InvalidOperationException(await DescribeStartFailureAsync(service));
        }

        using var client = fixture.App.CreateHttpClient(service);
        using var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), cts.Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>State, exit code and the resource's own log, so a failed start is diagnosable from the test output.</summary>
    private async Task<string> DescribeStartFailureAsync(string service)
    {
        var state = fixture.App.Services.GetRequiredService<ResourceNotificationService>().TryGetCurrentState(service, out var ev)
            ? $"state={ev!.Snapshot.State?.Text}; exit={ev.Snapshot.ExitCode}"
            : "no state";

        var lines = new List<string>();
        using var logTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            var batches = await fixture.App.Services.GetRequiredService<ResourceLoggerService>()
                .GetAllAsync(service).ToListAsync(logTimeout.Token);
            lines.AddRange(batches.SelectMany(b => b).Select(l => l.Content));
        }
        catch (OperationCanceledException)
        {
            // keep what was collected
        }

        return $"{service} failed to start ({state}). Resource log:{Environment.NewLine}{string.Join(Environment.NewLine, lines)}";
    }
}
