using Notifications.Contracts;

namespace Notifications.Core.Delivery;

/// <summary>Generic message for push and mail: static template text and the deep link, never data.</summary>
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
