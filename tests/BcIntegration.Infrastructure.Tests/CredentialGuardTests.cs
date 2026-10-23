using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Authorization;

namespace BcIntegration.Infrastructure.Tests;

public class CredentialGuardTests
{
    private static IConfiguration Config() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:bcintegration"] = "Host=localhost;Database=x",
        ["Bc:Credential"] = TokenCredentialFactory.LocalFake,
    }).Build();

    [Fact]
    public void Local_fake_credential_is_refused_in_production() =>
        Should.Throw<InvalidOperationException>(() =>
            new ServiceCollection().AddBcIntegrationInfrastructure(Config(), new TestHostEnvironment { EnvironmentName = "Production" }));

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void Local_fake_credential_is_allowed_locally(string environment) =>
        Should.NotThrow(() => new ServiceCollection().AddBcIntegrationInfrastructure(Config(), new TestHostEnvironment { EnvironmentName = environment }));
}
