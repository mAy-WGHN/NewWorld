using Notifications.Contracts;
using Notifications.Core.Templates;

namespace Notifications.Core.Delivery;

/// <summary>
/// Handles <see cref="SendNotification"/>: checks it (<see cref="NotificationPolicy"/>), then per recipient and channel stores the in-app
/// notification or hands a generic message to the push/mail adapter, honouring preferences (mandatory templates ignore them).
/// Idempotent: a recipient that already has this NotificationId is skipped. Adapter failures are recorded, not thrown: they must
/// not undo the in-app notification or hold back other recipients.
/// </summary>
public sealed class NotificationDispatcher(
    TemplateCatalog catalog,
    INotificationStore store,
    IPreferenceStore preferences,
    IPushSender push,
    IEmailSender email,
    TimeProvider time)
{
    public async Task DispatchAsync(SendNotification command, CancellationToken cancellationToken)
    {
        var check = NotificationPolicy.Check(command, catalog);
        if (check.Rejection is { } rejection)
        {
            throw new NotificationRejectedException(rejection);
        }

        var template = check.Template!;
        var recipients = command.RecipientEmployeeIds.Distinct().ToList();
        var channels = command.Channels.Distinct().ToList();
        var stored = await preferences.GetAsync(recipients, template.Key, cancellationToken);

        foreach (var employeeId in recipients)
        {
            if (await store.ExistsAsync(command.NotificationId, employeeId, cancellationToken))
            {
                continue;
            }

            foreach (var channel in channels)
            {
                var now = time.GetUtcNow();
                var enabled = template.Mandatory || stored.FirstOrDefault(p => p.EmployeeId == employeeId && p.Channel == channel)?.Enabled != false;
                var status = enabled ? await DeliverAsync(command, template, employeeId, channel, now, cancellationToken) : DeliveryStatus.SkippedByPreference;
                await store.AddDeliveryAsync(new DeliveryRecord(command.NotificationId, employeeId, channel, status, now), cancellationToken);
            }
        }
    }

    private async Task<DeliveryStatus> DeliverAsync(
        SendNotification command, Template template, Guid employeeId, NotificationChannel channel, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (channel == NotificationChannel.InApp)
        {
            var text = template.RenderInApp(command.Data);
            await store.AddInAppAsync(
                new InAppNotification(Guid.CreateVersion7(now), command.NotificationId, employeeId, template.Key, text.Title, text.Body, command.DeepLink, now, null),
                cancellationToken);
            return DeliveryStatus.Sent;
        }

        var generic = channel == NotificationChannel.Push ? template.Push : template.Email;
        var message = new GenericMessage(employeeId, generic.Title, generic.Body, command.DeepLink);
        try
        {
            await (channel == NotificationChannel.Push ? push.SendAsync(message, cancellationToken) : email.SendAsync(message, cancellationToken));
            return DeliveryStatus.Sent;
        }
#pragma warning disable CA1031 // delivery to external systems is best effort; the failure is recorded as status
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            return DeliveryStatus.Failed;
        }
    }
}
