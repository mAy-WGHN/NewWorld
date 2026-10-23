using Microsoft.EntityFrameworkCore;
using Notifications.Contracts;
using Notifications.Core.Delivery;

namespace Notifications.Infrastructure.Delivery;

public sealed class EfNotificationStore(NotificationsDbContext db) : INotificationStore
{
    public async Task<bool> ExistsAsync(Guid notificationId, Guid employeeId, CancellationToken cancellationToken) =>
        await db.Deliveries.AnyAsync(d => d.NotificationId == notificationId && d.EmployeeId == employeeId, cancellationToken)
        || db.Deliveries.Local.Any(d => d.NotificationId == notificationId && d.EmployeeId == employeeId);

    public Task AddInAppAsync(InAppNotification notification, CancellationToken cancellationToken)
    {
        db.InApp.Add(new InAppNotificationRecord
        {
            Id = notification.Id, NotificationId = notification.NotificationId, EmployeeId = notification.EmployeeId, TemplateKey = notification.TemplateKey,
            Title = notification.Title, Body = notification.Body, DeepLink = notification.DeepLink, CreatedAt = notification.CreatedAt, ReadAt = notification.ReadAt,
        });
        return Task.CompletedTask;
    }

    public Task AddDeliveryAsync(DeliveryRecord delivery, CancellationToken cancellationToken)
    {
        db.Deliveries.Add(new DeliveryRow { NotificationId = delivery.NotificationId, EmployeeId = delivery.EmployeeId, Channel = delivery.Channel.ToString(), Status = delivery.Status.ToString(), At = delivery.At });
        return Task.CompletedTask;
    }
}

public sealed class EfPreferenceStore(NotificationsDbContext db) : IPreferenceStore, IPreferenceWriter
{
    public async Task<IReadOnlyList<Preference>> GetAsync(IReadOnlyCollection<Guid> employeeIds, string templateKey, CancellationToken cancellationToken) =>
        (await db.Preferences.AsNoTracking().Where(p => employeeIds.Contains(p.EmployeeId) && p.TemplateKey == templateKey).ToListAsync(cancellationToken))
            .Select(p => new Preference(p.EmployeeId, p.TemplateKey, Enum.Parse<NotificationChannel>(p.Channel), p.Enabled))
            .ToList();

    public async Task SaveAsync(Preference preference, CancellationToken cancellationToken)
    {
        var channel = preference.Channel.ToString();
        var row = await db.Preferences.FindAsync([preference.EmployeeId, preference.TemplateKey, channel], cancellationToken);
        if (row is null)
        {
            row = new PreferenceRow { EmployeeId = preference.EmployeeId, TemplateKey = preference.TemplateKey, Channel = channel };
            db.Preferences.Add(row);
        }

        row.Enabled = preference.Enabled;
        await db.SaveChangesAsync(cancellationToken);
    }
}
