using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Shared.Testing;

namespace Services.Health.Tests;

/// <summary>Every service starts and reports health; readiness reflects the service's own database.</summary>
public sealed class ServiceHealthTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    public static TheoryData<string> Services => ["bcintegration", "access", "notifications", "workforce", "portal"];

    private static WebApplicationFactory<TMarker> Factory<TMarker>(string database, string connectionString) where TMarker : class =>
        new WebApplicationFactory<TMarker>().WithWebHostBuilder(host =>
        {
            host.UseEnvironment("Testing");
            // UseSetting (not ConfigureAppConfiguration): Program reads the connection string while building, before later callbacks run.
            host.UseSetting($"ConnectionStrings:{database}", connectionString);
        });

    private static async Task<(HttpStatusCode Live, HttpStatusCode Ready)> Probe<TMarker>(string database, string connectionString) where TMarker : class
    {
        await using var factory = Factory<TMarker>(database, connectionString);
        using var client = factory.CreateClient();
        using var live = await client.GetAsync(new Uri("/health/live", UriKind.Relative));
        using var ready = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));
        return (live.StatusCode, ready.StatusCode);
    }

    private static Task<(HttpStatusCode Live, HttpStatusCode Ready)> ProbeService(string service, string connectionString) => service switch
    {
        "bcintegration" => Probe<BcIntegration.Api.ApiMarker>(service, connectionString),
        "access" => Probe<Access.Api.ApiMarker>(service, connectionString),
        "notifications" => Probe<Notifications.Api.ApiMarker>(service, connectionString),
        "workforce" => Probe<Workforce.Api.ApiMarker>(service, connectionString),
        "portal" => Probe<Portal.Api.ApiMarker>(service, connectionString),
        _ => throw new ArgumentOutOfRangeException(nameof(service), service, null),
    };

    [Theory]
    [MemberData(nameof(Services))]
    public async Task Ready_and_live_are_ok_with_a_reachable_database(string service)
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        var (live, ready) = await ProbeService(service, connectionString);

        live.ShouldBe(HttpStatusCode.OK);
        ready.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [MemberData(nameof(Services))]
    public async Task Ready_fails_but_live_stays_ok_when_the_database_is_unreachable(string service)
    {
        const string unreachable = "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1;Pooling=false";

        var (live, ready) = await ProbeService(service, unreachable);

        live.ShouldBe(HttpStatusCode.OK);
        ready.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }
}
