namespace Notifications.Core.Delivery;

public interface INotificationStore
{
    /// <summary>The notification was already handled for this person (idempotency over the sender's NotificationId).</summary>
    Task<bool> ExistsAsync(Guid notificationId, Guid employeeId, CancellationToken cancellationToken);

    Task AddInAppAsync(InAppNotification notification, CancellationToken cancellationToken);

    Task AddDeliveryAsync(DeliveryRecord delivery, CancellationToken cancellationToken);
}

public interface IPreferenceStore
{
    Task<IReadOnlyList<Preference>> GetAsync(IReadOnlyCollection<Guid> employeeIds, string templateKey, CancellationToken cancellationToken);
}

/// <summary>Sends push. Receives only generic text; resolves the devices of the person itself. Real delivery is out of scope (fake adapter).</summary>
public interface IPushSender
{
    Task SendAsync(GenericMessage message, CancellationToken cancellationToken);
}

/// <summary>Sends mail with a generic subject. Real delivery is out of scope (fake adapter).</summary>
public interface IEmailSender
{
    Task SendAsync(GenericMessage message, CancellationToken cancellationToken);
}
