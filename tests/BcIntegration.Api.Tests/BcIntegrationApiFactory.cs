using BcIntegration.Api;
using BcIntegration.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shared.Testing;

namespace BcIntegration.Api.Tests;

/// <summary>The real API against its own PostgreSQL database, with the test auth handler instead of Entra.</summary>
public sealed class BcIntegrationApiFactory(string connectionString, Action<IServiceCollection>? configure = null) : WebApplicationFactory<ApiMarker>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting($"ConnectionStrings:{InfrastructureServiceCollectionExtensions.ConnectionName}", connectionString);
        builder.ConfigureTestServices(services =>
        {
            services.AddTestAuthentication();
            configure?.Invoke(services);
        });
    }
}
