using System.Net;
using System.Text;
using System.Text.Json;
using HrTool_API.Authentication;
using HrTool_API.Common.Api;
using HrTool_API.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace HrTool_API.Tests.Unit;

/// <summary>Ein Fake-HTTP-Handler, der die Anfragen aufzeichnet und je Aufruf eine vorbereitete Antwort (oder einen Fehler) liefert.</summary>
internal sealed class StubHandler : HttpMessageHandler, IHttpClientFactory
{
    public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];
    public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add((request, request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
        return Respond(request);
    }

    public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

public class HttpAccessDirectoryTests
{
    // Rollen-Rechte liefert je Recht auch Dimensionen, die das HR-Tool nicht kennt (reportIds des Portals): Sie werden ignoriert.
    private const string Privileges = """
        {"success":true,"data":{"privileges":["schedule:read","employee:read"],"scopes":{
          "schedule:read":{"all":false,"locationIds":["11111111-1111-1111-1111-111111111111"],"reportIds":["22222222-2222-2222-2222-222222222222"]},
          "employee:read":{"all":true,"locationIds":[],"reportIds":[]}},"roles":[]}}
        """;

    private static (HttpAccessDirectory Directory, StubHandler Handler) Create(int cacheSeconds = 10)
    {
        var handler = new StubHandler { Respond = _ => StubHandler.Json(Privileges) };
        var options = Options.Create(new AccessOptions { BaseUrl = "http://access.test", ApiKey = "k", CacheSeconds = cacheSeconds });
        return (new HttpAccessDirectory(handler, new MemoryCache(new MemoryCacheOptions()), options, NullLogger<HttpAccessDirectory>.Instance), handler);
    }

    [Fact]
    public async Task ResolvesPrivilegesAndScopes_WithTheTokenOfTheUser()
    {
        var (directory, handler) = Create();

        var access = await directory.ResolveAsync("the-token", default);

        Assert.NotNull(access);
        Assert.Contains("schedule:read", access.Privileges);
        Assert.False(access.Scopes["schedule:read"].All);
        Assert.Equal([Guid.Parse("11111111-1111-1111-1111-111111111111")], access.Scopes["schedule:read"].Locations);
        Assert.True(access.Scopes["employee:read"].All);
        var request = Assert.Single(handler.Requests).Request;
        Assert.Equal("http://access.test/api/me/privileges", request.RequestUri!.ToString());
        Assert.Equal("Bearer the-token", request.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task TheResultIsCachedPerToken_ForAFewSeconds()
    {
        var (directory, handler) = Create();

        await directory.ResolveAsync("a", default);
        await directory.ResolveAsync("a", default);
        await directory.ResolveAsync("b", default);

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task WithoutCache_EveryCallAsksAgain()
    {
        var (directory, handler) = Create(cacheSeconds: 0);

        await directory.ResolveAsync("a", default);
        await directory.ResolveAsync("a", default);

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task ARejectedToken_ReturnsNull_AndIsNotCached()
    {
        var (directory, handler) = Create();
        handler.Respond = _ => StubHandler.Json("""{"success":false,"message":"x","errors":[]}""", HttpStatusCode.Unauthorized);

        Assert.Null(await directory.ResolveAsync("a", default));
        Assert.Null(await directory.ResolveAsync("a", default));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task AnyOtherAnswer_MeansUnavailable(HttpStatusCode status)
    {
        var (directory, handler) = Create();
        handler.Respond = _ => new HttpResponseMessage(status);

        await Assert.ThrowsAsync<AccessUnavailableException>(() => directory.ResolveAsync("a", default));
    }

    [Fact]
    public async Task AnUnusableAnswer_OrANetworkError_MeansUnavailable()
    {
        var (directory, handler) = Create();
        handler.Respond = _ => StubHandler.Json("<html>kaputt</html>");
        await Assert.ThrowsAsync<AccessUnavailableException>(() => directory.ResolveAsync("a", default));

        handler.Respond = _ => throw new HttpRequestException("down");
        await Assert.ThrowsAsync<AccessUnavailableException>(() => directory.ResolveAsync("b", default));
    }

    [Fact]
    public async Task WithoutConfiguration_NothingIsGranted()
    {
        var handler = new StubHandler();
        var directory = new HttpAccessDirectory(handler, new MemoryCache(new MemoryCacheOptions()), Options.Create(new AccessOptions()), NullLogger<HttpAccessDirectory>.Instance);

        await Assert.ThrowsAsync<AccessUnavailableException>(() => directory.ResolveAsync("a", default));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void TheDefaultApplicationKey_IsHr()
    {
        Assert.Equal("hr", new AccessOptions().AppKey);
    }
}

public class HttpLocationDirectoryTests
{
    private static readonly Guid A = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static string Locations(string name) =>
        $$"""{"success":true,"data":[{"id":"{{A}}","name":"{{name}}","storeCode":"0010","archived":false}]}""";

    private static (HttpLocationDirectory Directory, StubHandler Handler, FakeTimeProvider Clock) Create()
    {
        var handler = new StubHandler { Respond = _ => StubHandler.Json(Locations("Nord")) };
        var clock = new FakeTimeProvider();
        var options = Options.Create(new AccessOptions { BaseUrl = "http://access.test", AppKey = "hr", ApiKey = "app-key" });
        return (new HttpLocationDirectory(handler, options, clock, NullLogger<HttpLocationDirectory>.Instance), handler, clock);
    }

    [Fact]
    public async Task LoadsAllLocations_WithTheApplicationKey()
    {
        var (directory, handler, _) = Create();

        var locations = await directory.GetAllAsync(default);

        Assert.Equal("Nord", locations[A].Name);
        var request = Assert.Single(handler.Requests).Request;
        Assert.Equal("http://access.test/api/apps/hr/locations", request.RequestUri!.ToString());
        Assert.Equal("app-key", request.Headers.GetValues("X-Api-Key").Single());
    }

    [Fact]
    public async Task TheListIsCachedForAMinute_ThenRefreshed()
    {
        var (directory, handler, clock) = Create();

        await directory.GetAllAsync(default);
        clock.Advance(TimeSpan.FromSeconds(59));
        await directory.GetAllAsync(default);
        Assert.Single(handler.Requests);

        handler.Respond = _ => StubHandler.Json(Locations("Süd"));
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal("Süd", (await directory.GetAllAsync(default))[A].Name);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task ForceRefresh_ReloadsAnUnknownLocation_ButNotMoreOftenThanEveryFiveSeconds()
    {
        var (directory, handler, clock) = Create();
        await directory.GetAllAsync(default);

        await directory.GetAllAsync(default, forceRefresh: true);
        Assert.Single(handler.Requests); // gerade erst geladen: kein neuer Aufruf

        clock.Advance(TimeSpan.FromSeconds(6));
        handler.Respond = _ => StubHandler.Json(Locations("Neu"));
        Assert.Equal("Neu", (await directory.GetAllAsync(default, forceRefresh: true))[A].Name);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task WhenAccessIsDown_TheLastKnownListKeepsWorking_AndIsRetriedSoon()
    {
        var (directory, handler, clock) = Create();
        await directory.GetAllAsync(default);
        clock.Advance(TimeSpan.FromMinutes(2));
        handler.Respond = _ => throw new HttpRequestException("down");

        Assert.Equal("Nord", (await directory.GetAllAsync(default))[A].Name);
        Assert.Equal("Nord", (await directory.GetAllAsync(default))[A].Name);
        Assert.Equal(2, handler.Requests.Count); // der zweite Aufruf nutzt die zuletzt bekannte Liste, ohne sofort wieder anzufragen

        handler.Respond = _ => StubHandler.Json(Locations("Neu"));
        clock.Advance(TimeSpan.FromSeconds(11));
        Assert.Equal("Neu", (await directory.GetAllAsync(default))[A].Name);
    }

    [Fact]
    public async Task WithoutAnyKnownList_AnOutageIsA503()
    {
        var (directory, handler, _) = Create();
        handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);

        var ex = await Assert.ThrowsAsync<ApiException>(() => directory.GetAllAsync(default));

        Assert.Equal(503, ex.StatusCode);
    }
}

public class CatalogRegistrationTests
{
    [Fact]
    public async Task RegistersTheCatalog_WithTheApplicationKey()
    {
        var handler = new StubHandler();
        var options = new AccessOptions { BaseUrl = "http://access.test", AppKey = "hr", ApiKey = "app-key" };
        var registration = new CatalogRegistration(handler, Options.Create(options), NullLogger<CatalogRegistration>.Instance);

        Assert.True(await registration.TryRegisterAsync(options, default));

        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("http://access.test/api/apps/hr/catalog", request.RequestUri!.ToString());
        Assert.Equal("app-key", request.Headers.GetValues("X-Api-Key").Single());
        var areas = JsonDocument.Parse(body).RootElement.GetProperty("areas").EnumerateArray().ToList();
        Assert.Equal(PrivilegeCatalog.All.Count, areas.Sum(a => a.GetProperty("actions").GetArrayLength()));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task ARefusal_IsNotASuccess_SoItIsTriedAgain(HttpStatusCode status)
    {
        var handler = new StubHandler { Respond = _ => new HttpResponseMessage(status) };
        var options = new AccessOptions { BaseUrl = "http://access.test", ApiKey = "app-key" };
        var registration = new CatalogRegistration(handler, Options.Create(options), NullLogger<CatalogRegistration>.Instance);

        Assert.False(await registration.TryRegisterAsync(options, default));
    }

    [Fact]
    public async Task AnUnreachableAccessService_IsNotASuccess_AndDoesNotThrow()
    {
        var handler = new StubHandler { Respond = _ => throw new HttpRequestException("down") };
        var options = new AccessOptions { BaseUrl = "http://access.test", ApiKey = "app-key" };
        var registration = new CatalogRegistration(handler, Options.Create(options), NullLogger<CatalogRegistration>.Instance);

        Assert.False(await registration.TryRegisterAsync(options, default));
    }
}
