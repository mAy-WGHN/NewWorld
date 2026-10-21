using System.Net;
using Benachrichtigungen.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Benachrichtigungen.Tests.Functional;

public class PushSubscriptionTests(ApiFactory factory) : ApiTestBase(factory)
{
    private static object Subscription(string endpoint, string p256dh = "key-1", string auth = "auth-1") =>
        new { endpoint, keys = new { p256dh, auth } };

    private static string NewEndpoint() => $"https://push.example/{Guid.NewGuid():N}";

    [Fact]
    public async Task AllEndpoints_RequireAuthentication()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync("/api/me/push-public-key")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync("/api/me/push-subscriptions", Subscription(NewEndpoint()))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await DeleteAsync("/api/me/push-subscriptions", body: new { endpoint = NewEndpoint() })).StatusCode);
    }

    [Fact]
    public async Task PublicKey_IsTheConfiguredVapidKey()
    {
        var (_, token) = NewUserWithToken();

        var data = await DataAsync(await GetAsync("/api/me/push-public-key", token));

        Assert.Equal(ApiFactory.VapidPublicKey, data.GetProperty("publicKey").GetString());
    }

    [Fact]
    public async Task Subscribe_CreatesASubscriptionForTheCurrentUser()
    {
        var (user, token) = NewUserWithToken();
        var endpoint = NewEndpoint();

        var response = await PostAsync("/api/me/push-subscriptions", Subscription(endpoint, "pk", "ak"), token);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await JsonAsync(response);
        Assert.True(json.GetProperty("success").GetBoolean());
        var stored = await QueryDbAsync(db => db.PushSubscriptions.AsNoTracking().SingleAsync(s => s.Endpoint == endpoint));
        Assert.Equal(user.Id, stored.UserId);
        Assert.Equal("pk", stored.PublicKey);
        Assert.Equal("ak", stored.AuthToken);
    }

    [Fact]
    public async Task Subscribe_TwiceWithTheSameEndpoint_IsAnUpsert()
    {
        var (_, token) = NewUserWithToken();
        var endpoint = NewEndpoint();

        await PostAsync("/api/me/push-subscriptions", Subscription(endpoint, "old", "old"), token);
        var second = await PostAsync("/api/me/push-subscriptions", Subscription(endpoint, "new", "new"), token);

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var rows = await QueryDbAsync(db => db.PushSubscriptions.AsNoTracking().Where(s => s.Endpoint == endpoint).ToListAsync());
        var row = Assert.Single(rows);
        Assert.Equal("new", row.PublicKey);
        Assert.Equal("new", row.AuthToken);
    }

    [Fact]
    public async Task Subscribe_TwoDevicesOfTheSameUser_CreateTwoRows()
    {
        var (user, token) = NewUserWithToken();

        await PostAsync("/api/me/push-subscriptions", Subscription(NewEndpoint()), token);
        await PostAsync("/api/me/push-subscriptions", Subscription(NewEndpoint()), token);

        var count = await QueryDbAsync(db => db.PushSubscriptions.CountAsync(s => s.UserId == user.Id));
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task Subscribe_OnTheSameEndpointByAnotherUser_TransfersOwnership()
    {
        // Geteilter Browser: der zuletzt angemeldete Admin übernimmt den Endpoint.
        var (_, tokenA) = NewUserWithToken();
        var (userB, tokenB) = NewUserWithToken();
        var endpoint = NewEndpoint();
        await PostAsync("/api/me/push-subscriptions", Subscription(endpoint), tokenA);

        await PostAsync("/api/me/push-subscriptions", Subscription(endpoint), tokenB);

        var stored = await QueryDbAsync(db => db.PushSubscriptions.AsNoTracking().SingleAsync(s => s.Endpoint == endpoint));
        Assert.Equal(userB.Id, stored.UserId);
    }

    [Fact]
    public async Task Subscribe_WithInvalidBody_Returns422WithNestedFieldNames()
    {
        var (_, token) = NewUserWithToken();

        var response = await PostAsync("/api/me/push-subscriptions", Subscription("keine-url", p256dh: "", auth: ""), token);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var fields = (await JsonAsync(response)).GetProperty("errors").EnumerateArray()
            .Select(e => e.GetProperty("field").GetString()).ToList();
        Assert.Contains("endpoint", fields);
        Assert.Contains("keys.p256dh", fields);
        Assert.Contains("keys.auth", fields);
    }

    [Fact]
    public async Task Unsubscribe_RemovesAnExistingSubscription()
    {
        var (_, token) = NewUserWithToken();
        var endpoint = NewEndpoint();
        await PostAsync("/api/me/push-subscriptions", Subscription(endpoint), token);

        var response = await DeleteAsync("/api/me/push-subscriptions", token, new { endpoint });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(await QueryDbAsync(db => db.PushSubscriptions.AnyAsync(s => s.Endpoint == endpoint)));
    }

    [Fact]
    public async Task Unsubscribe_OfAnUnknownEndpoint_Returns204AsNoOp()
    {
        var (_, token) = NewUserWithToken();

        var response = await DeleteAsync("/api/me/push-subscriptions", token, new { endpoint = NewEndpoint() });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Unsubscribe_DoesNotRemoveTheSubscriptionOfAnotherUser()
    {
        var (_, tokenA) = NewUserWithToken();
        var (_, tokenB) = NewUserWithToken();
        var endpoint = NewEndpoint();
        await PostAsync("/api/me/push-subscriptions", Subscription(endpoint), tokenA);

        var response = await DeleteAsync("/api/me/push-subscriptions", tokenB, new { endpoint });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(await QueryDbAsync(db => db.PushSubscriptions.AnyAsync(s => s.Endpoint == endpoint)));
    }
}
