using BC_Anbindung.BusinessCentral;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BC_Anbindung.Tests.Infrastructure;

/// <summary>Startet den Dienst mit einem Fake statt Business Central (kein Netzwerk, keine Datenbank nötig).</summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    public const string ApiKey = "test-api-key-0123456789abcdef0123456789";
    public const string OtherApiKey = "other-api-key-0123456789abcdef0123456789";

    public FakeBcClient Bc { get; } = new();

    public ApiFactory()
    {
        // Umgebungsvariablen statt UseSetting: Program liest die Konfiguration schon beim Bauen des Builders.
        Environment.SetEnvironmentVariable("Gateway__ApiKeys__Identity", ApiKey);
        Environment.SetEnvironmentVariable("Gateway__ApiKeys__Portal", OtherApiKey);
        foreach (var key in new[] { "ClientId", "ClientSecret", "Scope", "TenantId", "Environment", "CompanyName" })
            Environment.SetEnvironmentVariable($"BusinessCentral__{key}", "test");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IBcCodeunitClient>();
            services.AddSingleton<IBcCodeunitClient>(Bc);
        });
    }
}

[CollectionDefinition("Api")]
public class ApiCollection : ICollectionFixture<ApiFactory>;
