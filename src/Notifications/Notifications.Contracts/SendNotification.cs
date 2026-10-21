using Shared.Messaging;

namespace Notifications.Contracts;

/// <summary>
/// Order to notify people. The Notification service renders the template and delivers per channel; it has no
/// knowledge of the sender's domain. Idempotent via <paramref name="NotificationId"/>.
/// <paramref name="Data"/> may contain class 2 content only if <paramref name="Channels"/> is exactly <c>InApp</c>;
/// otherwise the command is rejected. Push and mail texts are generic and carry only <paramref name="DeepLink"/>.
/// </summary>
/// <remarks>Publisher: all business APIs (currently Workforce). Consumers: Notifications.</remarks>
/// <param name="MessageId">Unique message ID.</param>
/// <param name="CorrelationId">Correlates all messages of one flow.</param>
/// <param name="OccurredAt">Time of the order (TimeProvider).</param>
/// <param name="NotificationId">Idempotency key of the notification, chosen by the sender (stable across redeliveries).</param>
/// <param name="RecipientEmployeeIds">Recipients (Empfänger).</param>
/// <param name="TemplateKey">Template, scheme <c>&lt;service&gt;.&lt;occasion&gt;.&lt;recipient role&gt;</c>, e.g. <c>workforce.sick-note.store-manager</c>.</param>
/// <param name="Data">Placeholder values for the template.</param>
/// <param name="Channels">Requested channels.</param>
/// <param name="DeepLink">Relative link in the portal where the content is loaded after opening, e.g. <c>/absences/{id}</c>.</param>
[Sensitivity(SensitivityClass.SensitiveFacts)]
public sealed record SendNotification(
    Guid MessageId,
    Guid CorrelationId,
    DateTimeOffset OccurredAt,
    Guid NotificationId,
    IReadOnlyList<Guid> RecipientEmployeeIds,
    string TemplateKey,
    IReadOnlyDictionary<string, string> Data,
    IReadOnlyList<NotificationChannel> Channels,
    string? DeepLink) : ICommand;
