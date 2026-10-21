using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Shared.Messaging;

namespace Shared.Messaging.Tests;

public sealed class RecordingTransport : IMessageTransport
{
    public ConcurrentQueue<OutboundMessage> SentQueue { get; } = new();
    public IReadOnlyCollection<OutboundMessage> Sent => SentQueue.ToArray();
    public bool Fail { get; set; }

    public Task SendAsync(OutboundMessage message, CancellationToken cancellationToken)
    {
        if (Fail)
        {
            throw new InvalidOperationException("transport down; payload=" + message.Payload);
        }

        SentQueue.Enqueue(message);
        return Task.CompletedTask;
    }
}

public sealed class CapturingLogger<T> : ILogger<T>
{
    public List<string> Lines { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Lines.Add(formatter(state, exception));
        if (exception is not null)
        {
            Lines.Add(exception.ToString());
        }
    }
}
