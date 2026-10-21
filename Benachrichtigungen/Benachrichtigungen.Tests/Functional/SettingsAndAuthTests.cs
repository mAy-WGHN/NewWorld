using System.Net;
using System.Text.Json;
using Benachrichtigungen.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Benachrichtigungen.Tests.Functional;

/// <summary>Die eigenen Einstellungen des Users und die Anmeldung per JWT von "Rollen-Rechte" (dieser Dienst prüft das Token nur, er stellt keines aus).</summary>
public class SettingsAndAuthTests(ApiFactory factory) : ApiTestBase(factory)
{
    // --- Anmeldung ------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("/api/me/notification-settings")]
    [InlineData("/api/me/push-public-key")]
    public async Task UserEndpoints_RequireAToken(string url)
    {
        var response = await GetAsync(url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Equal("JWT Token not found", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task AGarbageToken_IsRejected_WithTheInvalidJwtMessage()
    {
        var response = await GetAsync("/api/me/notification-settings", "kein.echtes.token");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Invalid JWT Token", (await JsonAsync(response)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task AnExpiredToken_IsRejected_WithTheExpiredMessage()
    {
        var user = new TestUser(Guid.NewGuid(), "abgelaufen");

        var response = await GetAsync("/api/me/notification-settings", TokenFor(user, lifetime: TimeSpan.FromHours(-2)));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Expired JWT Token", (await JsonAsync(response)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task ATokenWithoutAUserId_AsSymfonyTokensAre_AsksToLogInAgain()
    {
        var user = new TestUser(Guid.NewGuid(), "alt");

        var response = await GetAsync("/api/me/notification-settings", TokenFor(user, withSub: false));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ATokenSignedWithAnotherKey_IsRejected()
    {
        var token = TokenFor(new TestUser(Guid.NewGuid(), "fremd")).Split('.');
        var forged = $"{token[0]}.{token[1]}.{new string('A', token[2].Length)}";

        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync("/api/me/notification-settings", forged)).StatusCode);
    }

    [Fact]
    public async Task ASendersApiKey_DoesNotOpenTheUserEndpoints()
    {
        var response = await SendAsync(HttpMethod.Get, "/api/me/notification-settings", configure: r => r.Headers.Add("X-Api-Key", ApiFactory.SourceKey));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PingAndHealth_NeedNoLogin()
    {
        Assert.Equal(HttpStatusCode.OK, (await GetAsync("/api/ping")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task UnknownRoutes_Return404InsteadOfAskingForALogin()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync("/api/gibt-es-nicht")).StatusCode);
    }

    // --- Einstellungen ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task WithoutARow_TheDefaultsApply()
    {
        var (_, token) = NewUserWithToken();

        var data = await DataAsync(await GetAsync("/api/me/notification-settings", token));

        Assert.True(data.GetProperty("bugNotificationsEnabled").GetBoolean());
        Assert.Equal("high", data.GetProperty("bugNotificationMinSeverity").GetString());
        Assert.False(data.GetProperty("theftNotificationsEnabled").GetBoolean());
        Assert.False(data.GetProperty("otherNotificationsEnabled").GetBoolean());
        Assert.False(data.GetProperty("suggestionNotificationsEnabled").GetBoolean());
        Assert.Equal([1, 2, 3, 4, 5, 6, 7], data.GetProperty("notificationWeekdays").EnumerateArray().Select(d => d.GetInt32()).ToArray());
    }

    [Fact]
    public async Task Update_ChangesOnlyTheGivenFields_AndPersistsThem()
    {
        var (user, token) = NewUserWithToken();

        var first = await DataAsync(await PatchAsync("/api/me/notification-settings", new
        {
            bugNotificationsEnabled = false, bugNotificationMinSeverity = "low", theftNotificationsEnabled = true, notificationWeekdays = new[] { 6, 7 }
        }, token));
        var second = await DataAsync(await PatchAsync("/api/me/notification-settings", new { suggestionNotificationsEnabled = true }, token));

        Assert.False(first.GetProperty("bugNotificationsEnabled").GetBoolean());
        Assert.Equal("low", first.GetProperty("bugNotificationMinSeverity").GetString());
        Assert.True(first.GetProperty("theftNotificationsEnabled").GetBoolean());
        Assert.True(second.GetProperty("suggestionNotificationsEnabled").GetBoolean());
        Assert.False(second.GetProperty("bugNotificationsEnabled").GetBoolean()); // unverändert
        var stored = await QueryDbAsync(db => db.UserNotificationSettings.AsNoTracking().SingleAsync(s => s.UserId == user.Id));
        Assert.Equal([6, 7], stored.NotificationWeekdays);
    }

    [Fact]
    public async Task Update_DeduplicatesWeekdays_AndAllowsAnEmptyList()
    {
        var (_, token) = NewUserWithToken();

        var deduplicated = await DataAsync(await PatchAsync("/api/me/notification-settings", new { notificationWeekdays = new[] { 3, 3, 1 } }, token));
        var empty = await DataAsync(await PatchAsync("/api/me/notification-settings", new { notificationWeekdays = Array.Empty<int>() }, token));

        Assert.Equal([3, 1], deduplicated.GetProperty("notificationWeekdays").EnumerateArray().Select(d => d.GetInt32()).ToArray());
        Assert.Empty(empty.GetProperty("notificationWeekdays").EnumerateArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    public async Task Update_WeekdaysOutsideOneToSeven_Return422(int weekday)
    {
        var (_, token) = NewUserWithToken();

        var response = await PatchAsync("/api/me/notification-settings", new { notificationWeekdays = new[] { 1, weekday } }, token);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("notificationWeekdays", (await JsonAsync(response)).GetProperty("errors")[0].GetProperty("field").GetString());
    }

    [Fact]
    public async Task Update_WithAnUnknownSeverity_Returns422()
    {
        var (_, token) = NewUserWithToken();

        var response = await PatchAsync("/api/me/notification-settings", new { bugNotificationMinSeverity = "mega" }, token);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task EveryUserHasHisOwnSettings()
    {
        var (_, tokenA) = NewUserWithToken();
        var (_, tokenB) = NewUserWithToken();

        await PatchAsync("/api/me/notification-settings", new { theftNotificationsEnabled = true }, tokenA);

        Assert.True((await DataAsync(await GetAsync("/api/me/notification-settings", tokenA))).GetProperty("theftNotificationsEnabled").GetBoolean());
        Assert.False((await DataAsync(await GetAsync("/api/me/notification-settings", tokenB))).GetProperty("theftNotificationsEnabled").GetBoolean());
    }

    [Fact]
    public async Task SettingsChosenViaTheApi_ControlWhoIsNotified()
    {
        var (user, token) = NewUserWithToken();
        await AddDeviceAsync(user.Id);
        Factory.Recipients.Allowed.Add(user.Id);
        await PatchAsync("/api/me/notification-settings", new { theftNotificationsEnabled = true }, token);

        var response = await SendEventAsync(new
        {
            id = Guid.NewGuid(), type = "report.created",
            data = new { reportId = Guid.NewGuid(), category = "theft", severity = "low", description = "Diebstahl", locationIds = Array.Empty<Guid>() }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(Factory.PushSender.Sent.SelectMany(s => s.Subscriptions), s => s.UserId == user.Id);
        Assert.NotEqual(JsonValueKind.Undefined, (await JsonAsync(response)).ValueKind);
    }
}
