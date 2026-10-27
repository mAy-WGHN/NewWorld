using Azure.Core;
using BcIntegration.Infrastructure.BusinessCentral;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Shared.Testing;
using WireMock.Server;

namespace BcIntegration.Infrastructure.Tests;

/// <summary>Issues token-1, token-2, ... so tests can see refreshes.</summary>
internal sealed class CountingCredential : TokenCredential
{
    private int _calls;

    public int Calls => _calls;

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => Issue();

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) => ValueTask.FromResult(Issue());

    private AccessToken Issue() => new($"token-{Interlocked.Increment(ref _calls)}", DateTimeOffset.MaxValue);
}

/// <summary>WireMock as BC with one scenario from tests/Fixtures/Bc, and the real BC client pipeline against it.</summary>
internal sealed class BcTestHost : IAsyncDisposable
{
    public const string CompanyId = "test-company";

    private BcTestHost(WireMockServer server, ServiceProvider services, CountingCredential credential)
    {
        Server = server;
        Services = services;
        Credential = credential;
    }

    public WireMockServer Server { get; }
    public ServiceProvider Services { get; }
    public CountingCredential Credential { get; }
    public BcClient Client => Services.GetRequiredService<BcClient>();

    /// <param name="scenario">Folder under tests/Fixtures/Bc.</param>
    /// <param name="overrides">Configuration values replacing the test defaults.</param>
    /// <param name="configure">Extra registrations before the service's own (e.g. replacements).</param>
    /// <param name="connectionString">With a database: the complete service infrastructure; without: only the BC client.</param>
    public static BcTestHost Start(
        string scenario,
        IDictionary<string, string?>? overrides = null,
        Action<IServiceCollection>? configure = null,
        string? connectionString = null)
    {
        var server = WireMockServer.Start();
        server.ReadStaticMappings(Path.Combine(RepositoryPaths.Root, "tests", "Fixtures", "Bc", scenario));

        var settings = new Dictionary<string, string?>
        {
            ["Bc:BaseUrl"] = server.Url + "/",
            ["Bc:CompanyId"] = CompanyId,
            ["Bc:StoresPath"] = "test/stores/v1.0/companies({companyId})/stores",
            ["Bc:AbsencesPath"] = "test/absences/v1.0/companies({companyId})/employeeAbsences",
            ["Bc:SoapBaseUrl"] = server.Url + "/WS/",
            ["Bc:CompanyName"] = CompanyId,
            ["Bc:Resilience:MaxRetries"] = "2",
            ["Bc:Resilience:BaseDelay"] = "00:00:00.010",
            ["Bc:Resilience:AttemptTimeout"] = "00:00:10", // generous: under load a cold first request can take over a second (the timeout test sets its own)
            ["Bc:Resilience:TotalTimeout"] = "00:00:20",
            ["Bc:RateLimit:PermitsPerSecond"] = "100",
        };
        if (connectionString is not null)
        {
            settings[$"ConnectionStrings:{InfrastructureServiceCollectionExtensions.ConnectionName}"] = connectionString;
        }

        foreach (var (key, value) in overrides ?? new Dictionary<string, string?>())
        {
            settings[key] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var credential = new CountingCredential();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKeyedSingleton<TokenCredential>(BcClientServiceCollectionExtensions.CredentialKey, credential);
        configure?.Invoke(services);
        if (connectionString is null)
        {
            services.AddBcClient(configuration);
        }
        else
        {
            services.AddBcIntegrationInfrastructure(configuration, new TestHostEnvironment());
        }

        return new BcTestHost(server, services.BuildServiceProvider(), credential);
    }

    public int RequestCount(string method) => Server.LogEntries.Count(e => e.RequestMessage!.Method == method);

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        Server.Stop();
        Server.Dispose();
    }
}

internal sealed class TestHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Testing";
    public string ApplicationName { get; set; } = "BcIntegration.Tests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
