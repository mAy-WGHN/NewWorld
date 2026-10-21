

namespace Shared.Infrastructure.Messaging;

public sealed class MessagingOptions
{
    public const string ConfigurationSection = "Messaging";

    /// <summary>How long processed outbox/inbox entries are kept (Datenschutz-Regel: 7 days by default).</summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(7);

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Fully qualified Service Bus namespace (managed identity). No shared access keys.</summary>
    public string? ServiceBusNamespace { get; set; }
}
