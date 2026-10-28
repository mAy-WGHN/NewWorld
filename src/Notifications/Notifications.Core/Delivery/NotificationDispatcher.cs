using Notifications.Contracts;
using Notifications.Core.Templates;

namespace Notifications.Core.Delivery;

/// <summary>
/// Handles <see cref="SendNotification"/>: checks it (<see cref="NotificationPolicy"/>), then per recipient and channel stores the in-app
/// notification or hands the rendered text to the push/mail adapter, honouring preferences (mandatory templates ignore them).
/// Idempotent: a recipient that already has this NotificationId is skipped. Adapter failures are recorded, not thrown: they must
/// not undo the in-app notification or hold back other recipients.
/// </summary>
public sealed class NotificationDispatcher(
    TemplateCatalog catalog,
    INotificationStore store,
    IPreferenceStore preferences,
    IPushSender push,
    IEmailSender email,
    TimeProvider time,
    IRecipientDirectory? names = null,
    ITemplateOverrides? overrides = null)
{
    public async Task DispatchAsync(SendNotification command, CancellationToken cancellationToken)
    {
        // The template in force: the file with the texts HR changed (which are checked again, a faulty change falls back to the file).
        var check = catalog.TryGet(command.TemplateKey, out var known)
            ? NotificationPolicy.Check(command, TemplateOverrideRules.Apply(known!, overrides is null ? null : (await overrides.GetAsync(known!.Key, cancellationToken))?.Texts))
            : NotificationPolicy.Check(command, catalog);
        if (check.Rejection is { } rejection)
        {
            throw new NotificationRejectedException(rejection);
        }

        var template = check.Template!;
        var recipients = command.RecipientEmployeeIds.Distinct().ToList();
        var channels = command.Channels.Distinct().ToList();
        var stored = await preferences.GetAsync(recipients, template.Key, cancellationToken);
        var needsName = channels.Any(c => Template.VariablesIn(template.TextFor(c)).Any(TemplateVariables.IsRecipient));
        var recipientNames = needsName && names is not null ? await names.NamesAsync(recipients, cancellationToken) : new Dictionary<Guid, RecipientName>();

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
                var status = enabled ? await DeliverAsync(command, template, employeeId, recipientNames.GetValueOrDefault(employeeId), channel, now, cancellationToken) : DeliveryStatus.SkippedByPreference;
                await store.AddDeliveryAsync(new DeliveryRecord(command.NotificationId, employeeId, channel, status, now), cancellationToken);
            }
        }
    }

    private async Task<DeliveryStatus> DeliverAsync(
        SendNotification command, Template template, Guid employeeId, RecipientName? recipient, NotificationChannel channel, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (channel == NotificationChannel.InApp)
        {
            var text = template.RenderInApp(command.Data, recipient);
            await store.AddInAppAsync(
                new InAppNotification(Guid.CreateVersion7(now), command.NotificationId, employeeId, template.Key, text.Title, text.Body, command.DeepLink, now, null),
                cancellationToken);
            return DeliveryStatus.Sent;
        }

        var rendered = template.Render(channel, command.Data, recipient);
        var message = new GenericMessage(employeeId, rendered.Title, rendered.Body, command.DeepLink);
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
