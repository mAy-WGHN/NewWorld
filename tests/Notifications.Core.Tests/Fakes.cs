using Notifications.Contracts;
using Notifications.Core.Delivery;

namespace Notifications.Core.Tests;

internal sealed class FakeNotificationStore : INotificationStore
{
    public List<InAppNotification> InApp { get; } = [];
    public List<DeliveryRecord> Deliveries { get; } = [];

    public Task<bool> ExistsAsync(Guid notificationId, Guid employeeId, CancellationToken cancellationToken) =>
        Task.FromResult(Deliveries.Any(d => d.NotificationId == notificationId && d.EmployeeId == employeeId));

    public Task AddInAppAsync(InAppNotification notification, CancellationToken cancellationToken)
    {
        InApp.Add(notification);
        return Task.CompletedTask;
    }

    public Task AddDeliveryAsync(DeliveryRecord delivery, CancellationToken cancellationToken)
    {
        Deliveries.Add(delivery);
        return Task.CompletedTask;
    }
}

internal sealed class FakePreferences : IPreferenceStore
{
    private readonly List<Preference> _rows = [];

    public void Set(Guid employeeId, string templateKey, NotificationChannel channel, bool enabled) =>
        _rows.Add(new Preference(employeeId, templateKey, channel, enabled));

    public Task<IReadOnlyList<Preference>> GetAsync(IReadOnlyCollection<Guid> employeeIds, string templateKey, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Preference>>(_rows.Where(r => employeeIds.Contains(r.EmployeeId) && r.TemplateKey == templateKey).ToList());
}

internal sealed class FakePush : IPushSender
{
    public List<GenericMessage> Sent { get; } = [];
    public bool Fail { get; set; }

    public Task SendAsync(GenericMessage message, CancellationToken cancellationToken)
    {
        if (Fail)
        {
            throw new InvalidOperationException("push service down");
        }

        Sent.Add(message);
        return Task.CompletedTask;
    }
}

internal sealed class FakeEmail : IEmailSender
{
    public List<GenericMessage> Sent { get; } = [];

    public Task SendAsync(GenericMessage message, CancellationToken cancellationToken)
    {
        Sent.Add(message);
        return Task.CompletedTask;
    }
}
