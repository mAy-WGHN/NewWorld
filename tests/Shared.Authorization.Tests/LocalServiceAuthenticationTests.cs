using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Shared.Authorization;

namespace Shared.Authorization.Tests;

public class LocalServiceAuthenticationTests
{
    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static IConfiguration Config(string? value) =>
        new ConfigurationBuilder().AddInMemoryCollection(value is null ? [] : new Dictionary<string, string?> { [LocalServiceAuthentication.ConfigurationKey] = value }).Build();

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void Is_enabled_only_when_switched_on(string environment)
    {
        LocalServiceAuthentication.IsEnabled(Config("true"), new Environment(environment)).ShouldBeTrue();
        LocalServiceAuthentication.IsEnabled(Config(null), new Environment(environment)).ShouldBeFalse();
        LocalServiceAuthentication.IsEnabled(Config("false"), new Environment(environment)).ShouldBeFalse();
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Switched_on_outside_development_fails_the_start(string environment) =>
        Should.Throw<InvalidOperationException>(() => LocalServiceAuthentication.IsEnabled(Config("true"), new Environment(environment)));

    [Theory]
    [InlineData("Bearer local-fake-token", true)]
    [InlineData("  Bearer local-fake-token ", true)]
    [InlineData("Bearer other", false)]
    [InlineData("bearer local-fake-token", false)]
    [InlineData("", false)]
    public void Only_the_static_token_counts(string header, bool expected) =>
        LocalServiceAuthentication.HasStaticToken(header).ShouldBe(expected);
}
