using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BC_Anbindung.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace BC_Anbindung.Tests.Functional;

[Collection("Api")]
public class GatewayTests
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public GatewayTests(ApiFactory factory)
    {
        _factory = factory;
        _factory.Bc.Reset();
        _client = factory.CreateClient();
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? body = null, string? apiKey = ApiFactory.ApiKey, HttpClient? client = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null) request.Content = JsonContent.Create(body);
        if (apiKey is not null) request.Headers.Add("X-Api-Key", apiKey);
        return (client ?? _client).SendAsync(request);
    }

    private Task<HttpResponseMessage> VerifyAsync(object body, string? apiKey = ApiFactory.ApiKey, HttpClient? client = null) =>
        SendAsync(HttpMethod.Post, "/api/employees/verify", body, apiKey, client);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    // --- Zugang: nur eigene Programme -------------------------------------------------------------------------

    [Fact]
    public async Task EveryEndpoint_RequiresAnApiKey()
    {
        foreach (var (method, url) in new[] { (HttpMethod.Post, "/api/employees/verify"), (HttpMethod.Get, "/api/stores") })
        {
            var missing = await SendAsync(method, url, new { employeeNo = "1", pin = "1" }, apiKey: null);
            var wrong = await SendAsync(method, url, new { employeeNo = "1", pin = "1" }, apiKey: "falsch");

            Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
            var body = await JsonAsync(missing);
            Assert.False(body.GetProperty("success").GetBoolean());
        }
    }

    [Fact]
    public async Task EveryConfiguredCaller_HasItsOwnKey()
    {
        _factory.Bc.Stores.Add(("0010", "Köln"));

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, "/api/stores", apiKey: ApiFactory.ApiKey)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, "/api/stores", apiKey: ApiFactory.OtherApiKey)).StatusCode);
    }

    [Fact]
    public async Task PingAndHealth_NeedNoKey()
    {
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, "/api/ping", apiKey: null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, "/health", apiKey: null)).StatusCode);
    }

    [Fact]
    public async Task UnknownRoutes_Return404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, "/api/gibt-es-nicht", apiKey: ApiFactory.ApiKey)).StatusCode);
    }

    // --- Personalnummer und PIN prüfen ----------------------------------------------------------------------------

    [Fact]
    public async Task Verify_WithTheRightPin_ReturnsTheEmployeeName()
    {
        _factory.Bc.AddEmployee("11207", "4711", "Anna Beispiel");

        var response = await VerifyAsync(new { employeeNo = " 11207 ", pin = "4711" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await JsonAsync(response)).GetProperty("data");
        Assert.True(data.GetProperty("authenticated").GetBoolean());
        Assert.Equal("Anna Beispiel", data.GetProperty("employeeName").GetString());
    }

    [Fact]
    public async Task Verify_AnswersInTheExactFormatTheCallersExpect()
    {
        // Vertrag mit "Rollen & Rechte" (HttpBcGateway): Dieselben Rohtexte stehen dort in den Tests. Ändert sich das Format, muss man beide anfassen.
        _factory.Bc.AddEmployee("11207", "4711", "Anna Beispiel");
        _factory.Bc.Stores.Add(("0010", "Köln"));

        var verify = await VerifyAsync(new { employeeNo = "11207", pin = "4711" });
        var refused = await VerifyAsync(new { employeeNo = "11207", pin = "0000" });
        await using var own = new ApiFactory();
        own.Bc.Stores.Add(("0010", "Köln"));
        var stores = await SendAsync(HttpMethod.Get, "/api/stores", client: own.CreateClient());

        Assert.Equal("""{"data":{"authenticated":true,"employeeName":"Anna Beispiel"},"success":true}""", await verify.Content.ReadAsStringAsync());
        Assert.Equal("""{"data":{"authenticated":false,"employeeName":null},"success":true}""", await refused.Content.ReadAsStringAsync());
        Assert.Equal("""{"data":[{"storeNo":"0010","name":"Köln"}],"success":true}""", await stores.Content.ReadAsStringAsync());
    }
    [Theory]
    [InlineData("11207", "0000")]
    [InlineData("99999", "4711")]
    public async Task Verify_WithAWrongPinOrUnknownEmployee_IsNotAuthenticated_ButStill200(string employeeNo, string pin)
    {
        _factory.Bc.AddEmployee("11207", "4711");

        var response = await VerifyAsync(new { employeeNo, pin });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await JsonAsync(response)).GetProperty("data");
        Assert.False(data.GetProperty("authenticated").GetBoolean());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("employeeName").ValueKind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Verify_WithAnEmptyPin_NeverAsksBc_BecauseBcWouldAcceptIt(string pin)
    {
        _factory.Bc.AddEmployee("11207", "");

        var response = await VerifyAsync(new { employeeNo = "11207", pin });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False((await JsonAsync(response)).GetProperty("data").GetProperty("authenticated").GetBoolean());
        Assert.Empty(_factory.Bc.StatusCalls);
    }

    [Fact]
    public async Task Verify_WhenBcIsDown_Returns503_WithoutLeakingAnythingFromBc()
    {
        _factory.Bc.Unavailable = true;

        var response = await VerifyAsync(new { employeeNo = "11207", pin = "4711" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Equal("Business Central ist nicht erreichbar.", JsonDocument.Parse(text).RootElement.GetProperty("message").GetString());
        Assert.DoesNotContain("geheimer-soap-body-mit-pin", text);
        Assert.DoesNotContain("4711", text);
    }

    [Fact]
    public async Task Verify_WithAnUnreadableBcAnswer_Returns503()
    {
        _factory.Bc.StatusOverride = "das ist kein json";

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await VerifyAsync(new { employeeNo = "11207", pin = "4711" })).StatusCode);
    }

    [Fact]
    public async Task Verify_ValidatesTheRequest()
    {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await VerifyAsync(new { pin = "1" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await VerifyAsync(new { employeeNo = "1" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await VerifyAsync(new { employeeNo = "", pin = "1" })).StatusCode);
    }

    // --- Filialen -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Stores_AreSortedByStoreNumber_AndEmptyOnesAreDropped()
    {
        // Der Zwischenspeicher gehört zur gemeinsamen Instanz: Eigene Instanz, damit andere Tests ihn nicht vorbelegt haben.
        await using var own = new ApiFactory();
        own.Bc.Stores.AddRange([("0020", "Berlin"), ("0010", " Köln "), ("", "Ohne Nummer"), ("0030", "")]);

        var response = await SendAsync(HttpMethod.Get, "/api/stores", client: own.CreateClient());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stores = (await JsonAsync(response)).GetProperty("data").EnumerateArray().ToList();
        Assert.Equal(["0010", "0020"], stores.Select(s => s.GetProperty("storeNo").GetString()));
        Assert.Equal("Köln", stores[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Stores_AreCachedBriefly_SoBcIsNotHitOnEveryCall()
    {
        await using var own = new ApiFactory();
        own.Bc.Stores.Add(("0010", "Köln"));
        var client = own.CreateClient();

        await SendAsync(HttpMethod.Get, "/api/stores", client: client);
        await SendAsync(HttpMethod.Get, "/api/stores", client: client);

        Assert.Equal(1, own.Bc.StoreCalls);
    }

    [Fact]
    public async Task Stores_WhenBcIsDown_Return503()
    {
        await using var own = new ApiFactory();
        own.Bc.Unavailable = true;

        var response = await SendAsync(HttpMethod.Get, "/api/stores", client: own.CreateClient());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    // --- Nicht konfiguriert ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task WithoutBcConfiguration_BothEndpointsAnswer503()
    {
        await using var own = new ApiFactory();
        var unconfigured = own.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BusinessCentral:ClientId"] = ""
            })));
        var client = unconfigured.CreateClient();

        var verify = await VerifyAsync(new { employeeNo = "1", pin = "1" }, client: client);
        var stores = await SendAsync(HttpMethod.Get, "/api/stores", client: client);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, verify.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, stores.StatusCode);
        Assert.Contains("nicht konfiguriert", (await JsonAsync(verify)).GetProperty("message").GetString());
    }
}
