using System.Net;
using System.Net.Http.Json;
using Access.Api;
using Access.Infrastructure;
using BcIntegration.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shared.Infrastructure.Messaging;
using Shared.Messaging;
using Shared.Testing;

namespace Access.Api.Tests;

/// <summary>The real Access API on its own database; people arrive as EmployeeChanged messages like in production.</summary>
public sealed class AccessApi : WebApplicationFactory<ApiMarker>
{
    public static readonly Guid F017 = Guid.Parse("00000000-0000-0000-0000-000000000017");
    public static readonly Guid F018 = Guid.Parse("00000000-0000-0000-0000-000000000018");
    public static readonly Guid Admin = Guid.Parse("00000000-0000-0000-0000-0000000000ff");
    public static readonly Guid Erika = Guid.Parse("00000000-0000-0000-0000-0000000000e1");
    public static readonly Guid Max = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    public static readonly Guid Tom = Guid.Parse("00000000-0000-0000-0000-0000000000a2");
    public const string AdminPersonnelNumber = "9000";

    private readonly string _connectionString;
    private readonly bool _realAuthentication;
    private readonly Action<IServiceCollection>? _configure;

    private AccessApi(string connectionString, bool realAuthentication, Action<IServiceCollection>? configure)
    {
        _connectionString = connectionString;
        _realAuthentication = realAuthentication;
        _configure = configure;
    }

    public HttpClient Http { get; private set; } = null!;

    /// <param name="postgres">Database container.</param>
    /// <param name="realAuthentication">Real tokens issued by Access instead of the test handler (login tests).</param>
    /// <param name="configure">Replacements, e.g. a fake BC login gateway.</param>
    public static async Task<AccessApi> StartAsync(PostgresFixture postgres, bool realAuthentication = false, Action<IServiceCollection>? configure = null)
    {
        var api = new AccessApi(await postgres.CreateDatabaseAsync(), realAuthentication, configure);
        api.Http = api.CreateClient();
        await api.ReceiveAsync(Admin, AdminPersonnelNumber, "Admin", null);
        await api.ReceiveAsync(Erika, "10042", "Erika Mustermann", F017);
        await api.ReceiveAsync(Max, "10001", "Max Muster", F017);
        await api.ReceiveAsync(Tom, "10002", "Tom Tester", F018);
        return api;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting($"ConnectionStrings:{InfrastructureServiceCollectionExtensions.ConnectionName}", _connectionString);
        builder.UseSetting("Access:BootstrapAdministrators:0", AdminPersonnelNumber);
        builder.UseSetting("Access:Login:PinHashIterations", "1000");
        builder.UseSetting("Access:Login:PinEncryptionKey", "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8="); // test key only
        builder.ConfigureTestServices(services =>
        {
            if (!_realAuthentication)
            {
                services.AddTestAuthentication();
            }

            _configure?.Invoke(services);
        });
    }

    /// <summary>Delivers an EmployeeChanged as BC Integration would.</summary>
    public async Task ReceiveAsync(Guid employeeId, string personnelNumber, string name, Guid? homeStore, DateOnly? exitDate = null)
    {
        var message = new EmployeeChanged(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UnixEpoch, employeeId, personnelNumber, name, homeStore,
            null, null, null, new DateOnly(2024, 1, 1), exitDate);
        var result = await Services.GetRequiredService<MessageDispatcher>().DispatchAsync(
            new InboundMessage(message.MessageId.ToString(), nameof(EmployeeChanged), MessageJson.Serialize(message), 1), CancellationToken.None);
        result.Outcome.ShouldBe(DispatchOutcome.Completed);
    }

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, Func<HttpRequestMessage, HttpRequestMessage>? identity, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        identity?.Invoke(request);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await Http.SendAsync(request);
    }

    public async Task<T> GetAsync<T>(string url, Func<HttpRequestMessage, HttpRequestMessage> identity)
    {
        using var response = await SendAsync(HttpMethod.Get, url, identity);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    public async Task<Guid> CreateAsync(string url, object body, Func<HttpRequestMessage, HttpRequestMessage> identity)
    {
        using var response = await SendAsync(HttpMethod.Post, url, identity, body);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<CreatedDto>())!.Id;
    }

    /// <summary>Registers the privileges Workforce will use (as the Workforce service would at start).</summary>
    public async Task RegisterWorkforceCatalogAsync()
    {
        using var response = await SendAsync(HttpMethod.Put, "/internal/apps/workforce/catalog", r => r.AsService(AccessRoles.CatalogRegister),
            new RegisterCatalogRequest(
            [
                new("absence:read", true), new("absence:read-reason", true), new("absence:approve", true), new("absence:read-own", false),
            ]));
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Http?.Dispose();
        }

        base.Dispose(disposing);
    }
}
