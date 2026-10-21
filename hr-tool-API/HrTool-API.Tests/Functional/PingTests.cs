using System.Net;
using HrTool_API.Tests.Infrastructure;

namespace HrTool_API.Tests.Functional;

public class PingTests(ApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Ping_IsPublic_AndReportsTheServerTime()
    {
        var response = await GetAsync("/api/ping");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.Equal("ok", body.GetProperty("status").GetString());
        Assert.True(DateTimeOffset.TryParse(body.GetProperty("time").GetString(), out _));
    }

    [Fact]
    public async Task Health_IsPublic()
    {
        Assert.Equal(HttpStatusCode.OK, (await GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task AnUnknownRoute_Is404WithTheErrorEnvelope()
    {
        var response = await GetAsync("/api/gibt-es-nicht");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False((await JsonAsync(response)).GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Cors_AllowsTheConfiguredOrigin_AndNotOthers()
    {
        var allowed = await SendAsync(HttpMethod.Get, "/api/ping", configure: r => r.Headers.Add("Origin", "http://localhost:5173"));
        var denied = await SendAsync(HttpMethod.Get, "/api/ping", configure: r => r.Headers.Add("Origin", "https://evil.example"));

        Assert.True(allowed.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(denied.Headers.Contains("Access-Control-Allow-Origin"));
    }
}
