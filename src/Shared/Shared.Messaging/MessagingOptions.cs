namespace Shared.Messaging;

public sealed class MessagingOptions
{
    /// <summary>How long processed outbox/inbox entries are kept (Datenschutz-Regel: 7 days by default).</summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(7);
}
