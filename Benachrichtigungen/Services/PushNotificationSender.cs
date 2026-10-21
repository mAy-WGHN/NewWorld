using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Benachrichtigungen.Data;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.EntityFrameworkCore;
using PushSubscriptionEntity = Benachrichtigungen.Domain.Entities.PushSubscription;

namespace Benachrichtigungen.Services;

/// <summary>Seam für den Push-Versand (in Tests ersetzbar).</summary>
public interface IPushNotificationSender
{
    Task SendToAllAsync(IReadOnlyCollection<PushSubscriptionEntity> subscriptions, string title, string body,
        string? url, CancellationToken ct);
}

/// <summary>
/// Web Push mit VAPID (RFC 8292), Content-Encoding aes128gcm (RFC 8291) und Urgency "high" (sonst bündeln/verzögern
/// z. B. Safari/iOS). Subscriptions, die der Push-Dienst mit 404/410 als gelöscht meldet, werden entfernt; andere
/// Fehler lassen die Zeile bestehen und werden nicht wiederholt.
/// </summary>
public class PushNotificationSender(
    PushServiceClient client, IConfiguration configuration, NotificationsDbContext db, ILogger<PushNotificationSender> logger)
    : IPushNotificationSender
{
    public async Task SendToAllAsync(IReadOnlyCollection<PushSubscriptionEntity> subscriptions, string title,
        string body, string? url, CancellationToken ct)
    {
        if (subscriptions.Count == 0) return;

        var publicKey = configuration["Vapid:PublicKey"];
        var privateKey = configuration["Vapid:PrivateKey"];
        var subject = configuration["Vapid:Subject"];
        if (string.IsNullOrEmpty(publicKey) || string.IsNullOrEmpty(privateKey) || string.IsNullOrEmpty(subject))
        {
            logger.LogWarning("Web Push übersprungen: Vapid-Konfiguration unvollständig.");
            return;
        }

        var vapid = new VapidAuthentication(publicKey, privateKey) { Subject = subject };
        var payload = BuildPayload(title, body, url);
        var expired = new ConcurrentBag<Guid>();

        // Parallel senden; der DbContext wird erst danach wieder benutzt (nicht threadsicher).
        await Parallel.ForEachAsync(subscriptions,
            new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct },
            async (subscription, token) =>
            {
                var target = new Lib.Net.Http.WebPush.PushSubscription
                {
                    Endpoint = subscription.Endpoint,
                    Keys = new Dictionary<string, string>
                    {
                        ["p256dh"] = subscription.PublicKey,
                        ["auth"] = subscription.AuthToken
                    }
                };
                var message = new PushMessage(payload) { Urgency = PushMessageUrgency.High };

                try
                {
                    await client.RequestPushMessageDeliveryAsync(target, message, vapid, token);
                }
                catch (PushServiceClientException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                {
                    expired.Add(subscription.Id);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Web Push an {Endpoint} fehlgeschlagen", subscription.Endpoint);
                }
            });

        if (!expired.IsEmpty)
        {
            var ids = expired.ToList();
            await db.PushSubscriptions.Where(s => ids.Contains(s.Id)).ExecuteDeleteAsync(ct);
        }
    }

    /// <summary>
    /// Angular-Service-Worker-Konvention (ngsw-worker.js): { notification: { title, body, data } }. Mit
    /// data.onActionClick fokussiert/öffnet ein Klick ein Fenster, auch wenn kein Tab offen ist. Nicht ändern.
    /// </summary>
    public static string BuildPayload(string title, string body, string? url)
    {
        object data = url is null
            ? new { url = (string?)null }
            : new { url, onActionClick = new { @default = new { operation = "navigateLastFocusedOrOpen", url } } };

        return JsonSerializer.Serialize(new { notification = new { title, body, data } });
    }
}
