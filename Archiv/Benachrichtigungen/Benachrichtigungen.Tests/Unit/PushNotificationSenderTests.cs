using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Benachrichtigungen.Domain.Entities;
using Benachrichtigungen.Services;
using Benachrichtigungen.Tests.Infrastructure;
using Lib.Net.Http.WebPush;
using PushSubscription = Benachrichtigungen.Domain.Entities.PushSubscription;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Benachrichtigungen.Tests.Unit;

/// <summary>
/// Der echte Sender gegen einen Fake-HTTP-Handler: Header (aes128gcm, Urgency high, VAPID), Bereinigung abgelaufener
/// Abos (404/410) und Fehlerisolation. Die Schlüssel sind echte P-256-Schlüssel, damit die Verschlüsselung läuft.
/// </summary>
[Collection("Api")]
public class PushNotificationSenderTests(ApiFactory factory)
{
    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public ConcurrentQueue<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Enqueue(request);
            return Task.FromResult(respond(request));
        }
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static (string Public, string Private) NewKeyPair()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = ecdsa.ExportParameters(includePrivateParameters: true);
        return (Base64Url([0x04, .. p.Q.X!, .. p.Q.Y!]), Base64Url(p.D!));
    }

    private static IConfiguration Config(bool withVapid = true)
    {
        var values = new Dictionary<string, string?>();
        if (withVapid)
        {
            var vapid = NewKeyPair();
            values["Vapid:PublicKey"] = vapid.Public;
            values["Vapid:PrivateKey"] = vapid.Private;
            values["Vapid:Subject"] = "mailto:test@example.com";
        }
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private async Task<List<PushSubscription>> SubscriptionsAsync(int count)
    {
        return await factory.WithDbAsync(async (db, sp) =>
        {
            var result = new List<PushSubscription>();
            for (var i = 0; i < count; i++)
            {
                var browser = NewKeyPair();
                var subscription = new PushSubscription
                {
                    UserId = Guid.NewGuid(),
                    Endpoint = $"https://push.example/{Guid.NewGuid():N}",
                    PublicKey = browser.Public,
                    AuthToken = Base64Url(RandomNumberGenerator.GetBytes(16))
                };
                db.PushSubscriptions.Add(subscription);
                result.Add(subscription);
            }
            await db.SaveChangesAsync();
            return result;
        });
    }

    private async Task<RecordingHandler> SendAsync(IReadOnlyCollection<PushSubscription> subscriptions,
        Func<HttpRequestMessage, HttpResponseMessage> respond, IConfiguration? config = null)
    {
        var handler = new RecordingHandler(respond);
        await factory.WithDbAsync(async (db, _) =>
        {
            var sender = new PushNotificationSender(
                new PushServiceClient(new HttpClient(handler)), config ?? Config(), db, NullLogger<PushNotificationSender>.Instance);
            await sender.SendToAllAsync(subscriptions, "Titel", "Text", "https://frontend.test/x", CancellationToken.None);
        });
        return handler;
    }

    private static HttpResponseMessage Created() => new(HttpStatusCode.Created);

    private Task<bool> ExistsAsync(Guid id) =>
        factory.WithDbAsync((db, _) => db.PushSubscriptions.AnyAsync(s => s.Id == id));

    [Fact]
    public async Task SendsOneRequestPerSubscription_ToItsEndpoint()
    {
        var subscriptions = await SubscriptionsAsync(3);

        var handler = await SendAsync(subscriptions, _ => Created());

        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(subscriptions.Select(s => s.Endpoint).Order(),
            handler.Requests.Select(r => r.RequestUri!.ToString()).Order());
        Assert.All(handler.Requests, r => Assert.Equal(HttpMethod.Post, r.Method));
    }

    [Fact]
    public async Task UsesAes128GcmContentEncoding_UrgencyHigh_AndVapidAuthorization()
    {
        var subscriptions = await SubscriptionsAsync(1);

        var handler = await SendAsync(subscriptions, _ => Created());

        var request = Assert.Single(handler.Requests);
        Assert.Equal("aes128gcm", Assert.Single(request.Content!.Headers.ContentEncoding));
        Assert.Equal("high", request.Headers.GetValues("Urgency").Single());
        Assert.Equal("vapid", request.Headers.Authorization!.Scheme.ToLowerInvariant());
        Assert.Contains("t=", request.Headers.Authorization.Parameter);
        Assert.Contains("k=", request.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task APushServiceAnswering410_RemovesTheSubscription()
    {
        var subscription = (await SubscriptionsAsync(1)).Single();

        await SendAsync([subscription], _ => new HttpResponseMessage(HttpStatusCode.Gone));

        Assert.False(await ExistsAsync(subscription.Id));
    }

    [Fact]
    public async Task APushServiceAnswering404_RemovesTheSubscription()
    {
        var subscription = (await SubscriptionsAsync(1)).Single();

        await SendAsync([subscription], _ => new HttpResponseMessage(HttpStatusCode.NotFound));

        Assert.False(await ExistsAsync(subscription.Id));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task OtherFailures_KeepTheSubscription_AndAreNotRetried(HttpStatusCode status)
    {
        var subscription = (await SubscriptionsAsync(1)).Single();

        var handler = await SendAsync([subscription], _ => new HttpResponseMessage(status));

        Assert.True(await ExistsAsync(subscription.Id));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ANetworkError_KeepsTheSubscription_AndDoesNotThrow()
    {
        var subscription = (await SubscriptionsAsync(1)).Single();

        var exception = await Record.ExceptionAsync(() =>
            SendAsync([subscription], _ => throw new HttpRequestException("Netzwerk weg")));

        Assert.Null(exception);
        Assert.True(await ExistsAsync(subscription.Id));
    }

    [Fact]
    public async Task OnlyTheExpiredSubscriptions_AreRemoved_WhenSendingToSeveral()
    {
        var subscriptions = await SubscriptionsAsync(3);
        var expired = subscriptions[1];

        await SendAsync(subscriptions, r =>
            r.RequestUri!.ToString() == expired.Endpoint ? new HttpResponseMessage(HttpStatusCode.Gone) : Created());

        Assert.True(await ExistsAsync(subscriptions[0].Id));
        Assert.False(await ExistsAsync(expired.Id));
        Assert.True(await ExistsAsync(subscriptions[2].Id));
    }

    [Fact]
    public async Task WithoutVapidConfiguration_NothingIsSent_AndNothingThrows()
    {
        var subscriptions = await SubscriptionsAsync(1);

        var handler = await SendAsync(subscriptions, _ => Created(), Config(withVapid: false));

        Assert.Empty(handler.Requests);
        Assert.True(await ExistsAsync(subscriptions[0].Id));
    }

    [Fact]
    public async Task WithNoSubscriptions_NoRequestIsMade()
    {
        var handler = await SendAsync([], _ => Created());

        Assert.Empty(handler.Requests);
    }

    // --- Payload ---------------------------------------------------------------------------------------------

    [Fact]
    public void Payload_FollowsTheAngularServiceWorkerConvention()
    {
        var json = JsonDocument.Parse(PushNotificationSender.BuildPayload("Neue Meldung: Bug (high)", "Kasse hängt",
            "https://frontend.test/wghn-admin/meldungen/bug")).RootElement;

        var notification = json.GetProperty("notification");
        Assert.Equal("Neue Meldung: Bug (high)", notification.GetProperty("title").GetString());
        Assert.Equal("Kasse hängt", notification.GetProperty("body").GetString());
        var data = notification.GetProperty("data");
        Assert.Equal("https://frontend.test/wghn-admin/meldungen/bug", data.GetProperty("url").GetString());
        var click = data.GetProperty("onActionClick").GetProperty("default");
        Assert.Equal("navigateLastFocusedOrOpen", click.GetProperty("operation").GetString());
        Assert.Equal("https://frontend.test/wghn-admin/meldungen/bug", click.GetProperty("url").GetString());
    }

    [Fact]
    public void Payload_WithoutUrl_ContainsOnlyANullUrl_AndNoClickAction()
    {
        var json = JsonDocument.Parse(PushNotificationSender.BuildPayload("T", "B", null)).RootElement;

        var data = json.GetProperty("notification").GetProperty("data");
        Assert.Equal(JsonValueKind.Null, data.GetProperty("url").ValueKind);
        Assert.False(data.TryGetProperty("onActionClick", out _));
    }
}
