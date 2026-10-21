using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Authorization;

namespace Shared.Authorization.Tests;

public class ConfigurationTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value)).Build();

    [Fact]
    public void Jwt_authentication_requires_an_audience()
    {
        var config = Config(("Authentication:Authority", "https://issuer.example/v2.0"));

        Should.Throw<InvalidOperationException>(() => new ServiceCollection().AddSharedJwtAuthentication(config));
    }

    [Fact]
    public void Jwt_authentication_requires_authority_or_tenant()
    {
        var config = Config(("Authentication:Audience", "api://portal"));

        Should.Throw<InvalidOperationException>(() => new ServiceCollection().AddSharedJwtAuthentication(config));
    }

    [Theory]
    [InlineData("Authentication:Authority", "https://issuer.example/v2.0")]
    [InlineData("Authentication:TenantId", "00000000-0000-0000-0000-000000000001")]
    public void Jwt_authentication_accepts_authority_or_tenant(string key, string value)
    {
        var config = Config(("Authentication:Audience", "api://portal"), (key, value));

        Should.NotThrow(() => new ServiceCollection().AddSharedJwtAuthentication(config));
    }

    [Fact]
    public void Credential_kind_is_selected_by_configuration()
    {
        TokenCredentialFactory.Create("ManagedIdentity").ShouldBeOfType<ManagedIdentityCredential>();
        TokenCredentialFactory.Create(null).ShouldBeOfType<DefaultAzureCredential>();
        Should.Throw<ArgumentException>(() => TokenCredentialFactory.Create("Password"));
    }
}
