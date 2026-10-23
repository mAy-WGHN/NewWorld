using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Notifications.Core.Delivery;

namespace Notifications.Infrastructure.Delivery;

/// <summary>Records what the fake adapters "sent". Tests read it; real delivery (push, mail) is out of scope.</summary>
public sealed class SentMessages
{
    public ConcurrentQueue<(string Channel, GenericMessage Message)> Items { get; } = new();
}

/// <summary>Fake push adapter: records the generic message. Logs only IDs, never texts.</summary>
public sealed partial class FakePushSender(SentMessages sent, ILogger<FakePushSender> logger) : IPushSender
{
    public Task SendAsync(GenericMessage message, CancellationToken cancellationToken)
    {
        sent.Items.Enqueue(("Push", message));
        LogSent(message.EmployeeId);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Fake push to {EmployeeId}")]
    private partial void LogSent(Guid employeeId);
}

/// <summary>Fake mail adapter: records the generic message. Logs only IDs, never texts.</summary>
public sealed partial class FakeEmailSender(SentMessages sent, ILogger<FakeEmailSender> logger) : IEmailSender
{
    public Task SendAsync(GenericMessage message, CancellationToken cancellationToken)
    {
        sent.Items.Enqueue(("Email", message));
        LogSent(message.EmployeeId);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Fake mail to {EmployeeId}")]
    private partial void LogSent(Guid employeeId);
}
