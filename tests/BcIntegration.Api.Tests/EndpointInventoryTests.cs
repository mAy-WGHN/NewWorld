using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shared.Testing;

namespace BcIntegration.Api.Tests;

/// <summary>BC Integration has no endpoints for frontends: everything except health requires a service role.</summary>
public sealed class EndpointInventoryTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Every_endpoint_except_health_requires_a_service_role()
    {
        await using var api = new BcIntegrationApiFactory(await postgres.CreateDatabaseAsync());
        using var _ = api.CreateClient();

        var endpoints = api.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
        var violations = endpoints
            .Where(e => e.RoutePattern.RawText?.StartsWith("/health", StringComparison.Ordinal) != true)
            .Where(e => !e.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(a => a.Policy?.StartsWith("App.Service.", StringComparison.Ordinal) == true))
            .Select(e => e.RoutePattern.RawText)
            .ToList();

        endpoints.ShouldNotBeEmpty();
        violations.ShouldBeEmpty();
    }
}
