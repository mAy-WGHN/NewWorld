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
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(["--ClamAv:Enabled=false", "--Ports:Fixed=false"], cts.Token);
        _app = await builder.BuildAsync(cts.Token);
        await _app.StartAsync(cts.Token);
    }

    /// <summary>
    /// Waits until the resource is healthy. A start that does not happen in time fails with the state of every resource and the
    /// log of the one waited for, so a slow or stuck start is diagnosable from the test output.
    /// </summary>
    public async Task WaitHealthyAsync(string service, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        try
        {
            await App.ResourceNotifications.WaitForResourceHealthyAsync(service, cts.Token);
        }
        catch (Exception e) when (e is DistributedApplicationException or OperationCanceledException)
        {
            throw new InvalidOperationException(await DescribeStartFailureAsync(service), e);
        }
    }

    private async Task<string> DescribeStartFailureAsync(string service)
    {
        var notifications = App.Services.GetRequiredService<ResourceNotificationService>();
        var states = App.Services.GetRequiredService<DistributedApplicationModel>().Resources.Select(r =>
            notifications.TryGetCurrentState(r.Name, out var ev)
                ? $"{r.Name}: {ev!.Snapshot.State?.Text ?? "(no state)"}; health={ev.Snapshot.HealthStatus}; exit={ev.Snapshot.ExitCode}; checks=[{string.Join(", ", ev.Snapshot.HealthReports.Select(h => $"{h.Name}={h.Status}"))}]"
                : $"{r.Name}: no state");

        // The log of the resource itself and of the Service Bus emulator (with its SQL container) every service waits for.
        var logs = new List<string>();
        foreach (var name in new[] { service, "servicebus", "servicebus-mssql" }.Distinct())
        {
            var lines = new List<string>();
            using var logTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try
            {
                var batches = await App.Services.GetRequiredService<ResourceLoggerService>()
                    .GetAllAsync(name).ToListAsync(logTimeout.Token);
                lines.AddRange(batches.SelectMany(b => b).Select(l => l.Content));
            }
            catch (OperationCanceledException)
            {
                // keep what was collected
            }

            logs.Add($"Log of {name}:{Environment.NewLine}{string.Join(Environment.NewLine, lines)}");
        }

        return $"{service} did not become healthy. Resources:{Environment.NewLine}{string.Join(Environment.NewLine, states)}{Environment.NewLine}{string.Join(Environment.NewLine, logs)}";
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
        await fixture.WaitHealthyAsync(service, ReadyTimeout, cts.Token);

        using var client = fixture.App.CreateHttpClient(service);
        using var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), cts.Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

}
