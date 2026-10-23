namespace Notifications.Infrastructure.Delivery;

public sealed class InAppNotificationRecord
{
    public Guid Id { get; set; }
    public Guid NotificationId { get; set; }
    public Guid EmployeeId { get; set; }
    public required string TemplateKey { get; set; }
    public required string Title { get; set; }
    public required string Body { get; set; }
    public string? DeepLink { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
}

public sealed class DeliveryRow
{
    public Guid NotificationId { get; set; }
    public Guid EmployeeId { get; set; }
    public required string Channel { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset At { get; set; }
}

public sealed class PreferenceRow
{
    public Guid EmployeeId { get; set; }
    public required string TemplateKey { get; set; }
    public required string Channel { get; set; }
    public bool Enabled { get; set; }
}
