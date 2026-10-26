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
    public void Jwt_authentication_requires_the_address_of_access_as_authority()
    {
        var config = Config(("Authentication:Audience", "newworld"));

        Should.Throw<InvalidOperationException>(() => new ServiceCollection().AddSharedJwtAuthentication(config));
    }

    [Fact]
    public void A_tenant_id_is_no_longer_a_way_to_configure_authentication()
    {
        var config = Config(("Authentication:Audience", "newworld"), ("Authentication:TenantId", "00000000-0000-0000-0000-000000000001"));

        Should.Throw<InvalidOperationException>(() => new ServiceCollection().AddSharedJwtAuthentication(config));
    }

    [Fact]
    public void Jwt_authentication_accepts_authority_and_audience()
    {
        var config = Config(("Authentication:Audience", "newworld"), ("Authentication:Authority", "https://access.example"));

        Should.NotThrow(() => new ServiceCollection().AddSharedJwtAuthentication(config));
    }

    [Fact]
    public void Credential_kind_is_selected_by_configuration()
    {
        TokenCredentialFactory.Create("ManagedIdentity").ShouldBeOfType<ManagedIdentityCredential>();
        TokenCredentialFactory.Create(null).ShouldBeOfType<DefaultAzureCredential>();
        TokenCredentialFactory.Create(TokenCredentialFactory.LocalFake)
            .GetToken(new Azure.Core.TokenRequestContext(["x/.default"]), CancellationToken.None).Token.ShouldBe("local-fake-token");
        Should.Throw<ArgumentException>(() => TokenCredentialFactory.Create("Password"));
    }
}
