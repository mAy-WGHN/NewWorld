using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Benachrichtigungen.Data;
using Benachrichtigungen.Domain.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace Benachrichtigungen.Tests.Infrastructure;

/// <summary>Ein User aus Sicht der Tests: Dieser Dienst kennt keine Benutzer-Tabelle, nur die ID aus dem Token.</summary>
public record TestUser(Guid Id, string Username);

[Collection("Api")]
public abstract class ApiTestBase
{
    protected ApiFactory Factory { get; }
    protected HttpClient Client { get; }

    protected ApiTestBase(ApiFactory factory)
    {
        Factory = factory;
        Factory.PushSender.Reset();
        Factory.Recipients.Reset();
        Factory.Clock.SetUtcNow(ApiFactory.Wednesday);
        Client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    protected static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..(prefix.Length + 9)];

    // --- Requests -------------------------------------------------------------------------------------------

    protected Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? body = null,
        string? token = null, Action<HttpRequestMessage>? configure = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null) request.Content = JsonContent.Create(body);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        configure?.Invoke(request);
        return Client.SendAsync(request);
    }

    protected Task<HttpResponseMessage> GetAsync(string url, string? token = null) =>
        SendAsync(HttpMethod.Get, url, null, token);

    protected Task<HttpResponseMessage> PostAsync(string url, object? body, string? token = null) =>
        SendAsync(HttpMethod.Post, url, body ?? new { }, token);

    protected Task<HttpResponseMessage> PatchAsync(string url, object body, string? token = null) =>
        SendAsync(HttpMethod.Patch, url, body, token);

    protected Task<HttpResponseMessage> DeleteAsync(string url, string? token = null, object? body = null) =>
        SendAsync(HttpMethod.Delete, url, body, token);

    /// <summary>Sendet ein Ereignis wie das Portal: mit dem API-Schlüssel des Absenders (<paramref name="key"/> = null: ohne Schlüssel).</summary>
    protected Task<HttpResponseMessage> SendEventAsync(object body, string? key = ApiFactory.SourceKey) =>
        SendAsync(HttpMethod.Post, "/api/events", body, configure: r =>
        {
            if (key is not null) r.Headers.Add("X-Api-Key", key);
        });

    protected static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    protected static async Task<JsonElement> DataAsync(HttpResponseMessage response) =>
        (await JsonAsync(response)).GetProperty("data");

    // --- Hilfsdaten ----------------------------------------------------------------------------------------------

    /// <summary>Ein JWT wie es "Rollen-Rechte" ausstellt (HS256 mit dem Testschlüssel): <c>username</c>, <c>sub</c> (User-ID) und <c>exp</c>.</summary>
    protected static string TokenFor(TestUser user, TimeSpan? lifetime = null, bool withSub = true)
    {
        var claims = new Dictionary<string, object> { ["username"] = user.Username };
        if (withSub) claims["sub"] = user.Id.ToString();
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Claims = claims,
            Expires = DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromHours(1)),
            NotBefore = DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromHours(1)).AddDays(-1), // vor dem Ablauf, sonst ist schon die Lebensdauer ungültig
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ApiFactory.JwtKey)), SecurityAlgorithms.HmacSha256)
        });
    }

    protected static (TestUser User, string Token) NewUserWithToken()
    {
        var user = new TestUser(Guid.NewGuid(), Unique("user"));
        return (user, TokenFor(user));
    }

    /// <summary>Legt ein Push-Gerät für den User an und liefert seine Zeile.</summary>
    protected Task<PushSubscription> AddDeviceAsync(Guid userId) =>
        Factory.WithDbAsync(async (db, _) =>
        {
            var subscription = new PushSubscription
            {
                UserId = userId,
                Endpoint = $"https://push.example/{Guid.NewGuid():N}",
                PublicKey = "p256dh-key",
                AuthToken = "auth-secret"
            };
            db.PushSubscriptions.Add(subscription);
            await db.SaveChangesAsync();
            return subscription;
        });

    /// <summary>Ein User mit einem Gerät, der die Meldung sehen darf (laut Fake von Rollen-Rechte) und optional abweichenden Einstellungen.</summary>
    protected async Task<TestUser> RecipientAsync(Action<UserNotificationSettings>? configure = null, int devices = 1, bool allowed = true)
    {
        var user = new TestUser(Guid.NewGuid(), Unique("empf"));
        for (var i = 0; i < devices; i++) await AddDeviceAsync(user.Id);
        if (configure is not null)
            await Factory.WithDbAsync(async (db, _) =>
            {
                var settings = new UserNotificationSettings { UserId = user.Id };
                configure(settings);
                db.UserNotificationSettings.Add(settings);
                await db.SaveChangesAsync();
            });
        if (allowed) Factory.Recipients.Allowed.Add(user.Id);
        return user;
    }

    protected Task<T> QueryDbAsync<T>(Func<NotificationsDbContext, Task<T>> query) =>
        Factory.WithDbAsync((db, _) => query(db));
}
