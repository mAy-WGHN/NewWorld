using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rollen_Rechte.Services;

namespace Rollen_Rechte.Tests.Unit;

public class HttpBcGatewayTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
            return await respond(request);
        }
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object body) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };

    private static HttpBcGateway Gateway(StubHandler handler, string baseUrl = "http://bc.test/", string apiKey = "geheim") =>
        new(new StubFactory(handler), Options.Create(new BcGatewayOptions { BaseUrl = baseUrl, ApiKey = apiKey }), NullLogger<HttpBcGateway>.Instance);

    [Fact]
    public async Task Verify_SendsTheApiKey_AndReadsTheEnvelope()
    {
        var handler = new StubHandler(_ => Task.FromResult(Json(HttpStatusCode.OK, new { success = true, data = new { authenticated = true, employeeName = "Anna" } })));

        var result = await Gateway(handler).VerifyEmployeeAsync("11207", "4711", CancellationToken.None);

        Assert.True(result.Authenticated);
        Assert.Equal("Anna", result.EmployeeName);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("http://bc.test/api/employees/verify", request.RequestUri!.ToString());
        Assert.Equal("geheim", request.Headers.GetValues("X-Api-Key").Single());
    }

    [Fact]
    public async Task Verify_AnAnswerOfNotAuthenticated_IsNotAnError()
    {
        var handler = new StubHandler(_ => Task.FromResult(Json(HttpStatusCode.OK, new { success = true, data = new { authenticated = false, employeeName = (string?)null } })));

        var result = await Gateway(handler).VerifyEmployeeAsync("11207", "0000", CancellationToken.None);

        Assert.False(result.Authenticated);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task AnyOtherStatus_IsUnavailable_AndNeverLeaksTheBody(HttpStatusCode status)
    {
        var handler = new StubHandler(_ => Task.FromResult(Json(status, new { success = false, message = "geheim-4711" })));

        var ex = await Assert.ThrowsAsync<BcGatewayUnavailableException>(() => Gateway(handler).VerifyEmployeeAsync("11207", "4711", CancellationToken.None));

        Assert.DoesNotContain("4711", ex.Message);
        Assert.DoesNotContain("geheim", ex.Message);
    }

    [Fact]
    public async Task ANetworkError_IsUnavailable()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("kein Netz"));

        await Assert.ThrowsAsync<BcGatewayUnavailableException>(() => Gateway(handler).GetStoresAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AnUnreadableAnswer_IsUnavailable()
    {
        var handler = new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("kein json") }));

        await Assert.ThrowsAsync<BcGatewayUnavailableException>(() => Gateway(handler).GetStoresAsync(CancellationToken.None));
    }

    [Fact]
    public async Task WithoutConfiguration_NothingIsSent()
    {
        var handler = new StubHandler(_ => Task.FromResult(Json(HttpStatusCode.OK, new { })));
        var gateway = Gateway(handler, baseUrl: "", apiKey: "");

        Assert.False(gateway.IsConfigured);
        await Assert.ThrowsAsync<BcGatewayUnavailableException>(() => gateway.VerifyEmployeeAsync("1", "1", CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Stores_AreReadFromTheEnvelope()
    {
        var handler = new StubHandler(_ => Task.FromResult(Json(HttpStatusCode.OK,
            new { success = true, data = new[] { new { storeNo = "0010", name = "Köln" }, new { storeNo = "0020", name = "Berlin" } } })));

        var stores = await Gateway(handler, baseUrl: "http://bc.test").GetStoresAsync(CancellationToken.None);

        Assert.Equal(["0010", "0020"], stores.Select(s => s.StoreNo));
        Assert.Equal("http://bc.test/api/stores", handler.Requests.Single().RequestUri!.ToString());
    }

    [Fact]
    public async Task ACallerCancellation_IsNotReportedAsUnavailable()
    {
        using var cts = new CancellationTokenSource();
        var handler = new StubHandler(async _ =>
        {
            await cts.CancelAsync();
            throw new TaskCanceledException();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Gateway(handler).GetStoresAsync(cts.Token));
    }

    [Fact]
    public async Task ReadsTheExactRawFormatOfTheBcService()
    {
        // Vertrag mit dem Dienst "BC-Anbindung": Dieselben Rohtexte stehen dort in den Tests (GatewayTests). Ändert sich das Format, muss man beide anfassen.
        var verify = """{"data":{"authenticated":true,"employeeName":"Anna Beispiel"},"success":true}""";
        var stores = """{"data":[{"storeNo":"0010","name":"Köln"}],"success":true}""";
        var handler = new StubHandler(request => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(request.RequestUri!.AbsolutePath.EndsWith("verify") ? verify : stores, Encoding.UTF8, "application/json")
        }));
        var gateway = Gateway(handler);

        var check = await gateway.VerifyEmployeeAsync("11207", "4711", CancellationToken.None);
        var list = await gateway.GetStoresAsync(CancellationToken.None);

        Assert.Equal(new BcEmployeeCheck(true, "Anna Beispiel"), check);
        Assert.Equal([new BcStore("0010", "Köln")], list);
        Assert.Contains("\"employeeNo\":\"11207\"", handler.Bodies[0]); // Feldnamen der Anfrage
        Assert.Contains("\"pin\":\"4711\"", handler.Bodies[0]);
    }}
