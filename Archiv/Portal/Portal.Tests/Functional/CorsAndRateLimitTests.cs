using System.Net;
using Portal.Tests.Infrastructure;

namespace Portal.Tests.Functional;

public class CorsAndRateLimitTests(ApiFactory factory) : ApiTestBase(factory)
{
    private const string AllowedOrigin = "http://localhost:4200";

    private Task<HttpResponseMessage> WithOrigin(HttpMethod method, string url, string origin, object? body = null,
        string? token = null) =>
        SendAsync(method, url, body, token, r => r.Headers.Add("Origin", origin));

    private Task<HttpResponseMessage> Preflight(string origin = AllowedOrigin) =>
        SendAsync(HttpMethod.Options, "/api/reports", configure: r =>
        {
            r.Headers.Add("Origin", origin);
            r.Headers.Add("Access-Control-Request-Method", "POST");
            r.Headers.Add("Access-Control-Request-Headers", "content-type,authorization");
        });

    // --- CORS ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Preflight_ForAnAllowedOrigin_ReturnsTheConfiguredPolicy()
    {
        var response = await Preflight();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal(AllowedOrigin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        var methods = string.Join(",", response.Headers.GetValues("Access-Control-Allow-Methods"));
        Assert.Contains("PATCH", methods);
        Assert.Contains("DELETE", methods);
        var headers = string.Join(",", response.Headers.GetValues("Access-Control-Allow-Headers")).ToLowerInvariant();
        Assert.Contains("authorization", headers);
        Assert.Contains("content-type", headers);
        Assert.Equal("3600", response.Headers.GetValues("Access-Control-Max-Age").Single());
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Theory]
    [InlineData("http://127.0.0.1:8080")]
    [InlineData("https://localhost")]
    public async Task Preflight_MatchesTheOriginRegex(string origin)
    {
        var response = await Preflight(origin);

        Assert.Equal(origin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task Preflight_ForAnUnknownOrigin_GetsNoAllowOriginHeader()
    {
        var response = await Preflight("https://evil.example");

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task ErrorResponses_KeepTheCorsHeader()
    {
        var (_, token) = await AdminWithTokenAsync();

        var notFound = await WithOrigin(HttpMethod.Get, $"/api/reports/{Guid.NewGuid()}", AllowedOrigin, null, token); // ApiException → Fehler-Handler
        var unauthorized = await WithOrigin(HttpMethod.Get, "/api/reports", AllowedOrigin);                          // 401-Challenge
        var invalid = await WithOrigin(HttpMethod.Post, "/api/faqs", AllowedOrigin, new { title = "" }, token);   // 422
        var unknownRoute = await WithOrigin(HttpMethod.Get, "/api/gibt-es-nicht", AllowedOrigin);                // Status-Page

        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownRoute.StatusCode);
        foreach (var response in new[] { notFound, unauthorized, invalid, unknownRoute })
            Assert.Equal(AllowedOrigin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task TooManyRequests_KeepsTheCorsHeader()
    {
        HttpResponseMessage? last = null;
        for (var i = 0; i < 101; i++) last = await WithOrigin(HttpMethod.Get, "/api/faqs", AllowedOrigin);

        Assert.Equal(HttpStatusCode.TooManyRequests, last!.StatusCode);
        Assert.Equal(AllowedOrigin, last.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    // --- Globales Rate-Limit -----------------------------------------------------------------------------------

    [Fact]
    public async Task GlobalLimit_Returns429AfterOneHundredRequestsPerMinute_WithRetryAfter()
    {
        var statuses = new List<HttpStatusCode>();
        HttpResponseMessage? rejected = null;
        for (var i = 0; i < 101; i++)
        {
            var response = await GetAsync("/api/faqs");
            statuses.Add(response.StatusCode);
            if (response.StatusCode == HttpStatusCode.TooManyRequests) rejected = response;
        }

        Assert.All(statuses.Take(100), s => Assert.Equal(HttpStatusCode.OK, s));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[100]);
        Assert.NotNull(rejected);
        Assert.True(int.Parse(rejected.Headers.GetValues("Retry-After").Single()) >= 1);
        var json = await JsonAsync(rejected);
        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.Equal("Zu viele Anfragen, bitte in Kürze erneut versuchen.", json.GetProperty("message").GetString());
    }

    [Fact]
    public async Task GlobalLimit_DoesNotCountCorsPreflightRequests()
    {
        for (var i = 0; i < 110; i++)
        {
            var response = await Preflight();
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }

        // Das Budget ist unberührt: normale Requests laufen weiterhin.
        Assert.Equal(HttpStatusCode.OK, (await GetAsync("/api/faqs")).StatusCode);
    }

    [Fact]
    public async Task GlobalLimit_IsPerClientIp()
    {
        for (var i = 0; i < 101; i++) await GetAsync("/api/faqs");
        UseNewIp();

        var response = await GetAsync("/api/faqs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
