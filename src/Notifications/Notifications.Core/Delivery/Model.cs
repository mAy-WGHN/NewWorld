using Notifications.Contracts;

namespace Notifications.Core.Delivery;

/// <summary>Text for push and mail with the deep link. It holds only what the template allows for the channel (class 1; class 2 only in the mail text of templates that allow it).</summary>
public sealed record GenericMessage(Guid EmployeeId, string Title, string Body, string? DeepLink);

/// <summary>A notification in the portal ("meine Benachrichtigungen").</summary>
public sealed record InAppNotification(
    Guid Id, Guid NotificationId, Guid EmployeeId, string TemplateKey, string Title, string Body, string? DeepLink,
    DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);

public enum DeliveryStatus
{
    Unknown = 0,
    Sent = 1,
    SkippedByPreference = 2,
    Failed = 3,
}

/// <summary>Zustellstatus per notification, person and channel. Also the idempotency record.</summary>
public sealed record DeliveryRecord(Guid NotificationId, Guid EmployeeId, NotificationChannel Channel, DeliveryStatus Status, DateTimeOffset At);

/// <summary>A person's choice per notification type (template) and channel; absent = enabled.</summary>
public sealed record Preference(Guid EmployeeId, string TemplateKey, NotificationChannel Channel, bool Enabled);
