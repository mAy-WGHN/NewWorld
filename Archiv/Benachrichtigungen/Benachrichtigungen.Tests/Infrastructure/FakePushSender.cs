using System.Collections.Concurrent;
using Benachrichtigungen.Domain.Entities;
using Benachrichtigungen.Services;

namespace Benachrichtigungen.Tests.Infrastructure;

public record SentPush(IReadOnlyList<PushSubscription> Subscriptions, string Title, string Body, string? Url);

/// <summary>Ersetzt den echten Push-Versand: Notifier-Tests laufen ohne Netzwerk.</summary>
public class FakePushSender : IPushNotificationSender
{
    private readonly ConcurrentQueue<SentPush> _sent = new();

    public IReadOnlyCollection<SentPush> Sent => _sent;
    public Exception? ThrowOnSend { get; set; }

    public Task SendToAllAsync(IReadOnlyCollection<PushSubscription> subscriptions, string title, string body,
        string? url, CancellationToken ct)
    {
        if (ThrowOnSend is not null) throw ThrowOnSend;
        _sent.Enqueue(new SentPush(subscriptions.ToList(), title, body, url));
        return Task.CompletedTask;
    }

    public void Reset()
    {
        ThrowOnSend = null;
        _sent.Clear();
    }
}
