using BcIntegration.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Notifications.Contracts;
using Notifications.Core.Delivery;
using Notifications.Core.Templates;
using Notifications.Infrastructure.Delivery;
using Shared.Authorization;
using Shared.Infrastructure.Messaging;

namespace Notifications.Infrastructure.Messaging;

/// <summary>
/// The only business message of this service. A refused command (protection rule, unknown template, ...) goes to the dead-letter
/// queue with its reason code; logs carry type, IDs and the code, never the data.
/// </summary>
public sealed partial class SendNotificationHandler(NotificationDispatcher dispatcher, ILogger<SendNotificationHandler> logger) : IMessageHandler<SendNotification>
{
    public async Task HandleAsync(SendNotification message, CancellationToken cancellationToken)
    {
        try
        {
            await dispatcher.DispatchAsync(message, cancellationToken);
        }
        catch (NotificationRejectedException ex)
        {
            LogRejected(message.MessageId, message.NotificationId, ex.Reason);
            throw new PermanentMessageFailureException(ex.Reason.ToString());
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "SendNotification {MessageId} (notification {NotificationId}) rejected: {Reason}")]
    private partial void LogRejected(Guid messageId, Guid notificationId, Rejection reason);
}

/// <summary>
/// Keeps the copy of the name of every employee (for <c>$USER.firstName</c>). Only the name and the exit date are kept; older messages are ignored.
/// TODO(BC-Modell): the display name is split by <see cref="EmployeeNames"/> until BC delivers first and last name separately.
/// </summary>
public sealed class EmployeeChangedHandler(NotificationsDbContext db) : IMessageHandler<EmployeeChanged>
{
    public async Task HandleAsync(EmployeeChanged message, CancellationToken cancellationToken)
    {
        var name = EmployeeNames.Split(message.Name);
        var row = await db.EmployeeNames.FindAsync([message.EmployeeId], cancellationToken);
        if (row is null)
        {
            db.EmployeeNames.Add(new EmployeeNameRecord { EmployeeId = message.EmployeeId, FirstName = name.FirstName, LastName = name.LastName, ExitDate = message.ExitDate, ChangedAt = message.OccurredAt });
        }
        else if (message.OccurredAt > row.ChangedAt)
        {
            row.FirstName = name.FirstName;
            row.LastName = name.LastName;
            row.ExitDate = message.ExitDate;
            row.ChangedAt = message.OccurredAt;
        }
    }
}

/// <summary>Deletes everything about a person: notifications, delivery records, preferences, the name copy and the permission copy; the person's name as author of a text change is anonymised.</summary>
public sealed class EmployeeDataErasureRequestedHandler(NotificationsDbContext db, PermissionsChangedHandler permissions) : IMessageHandler<EmployeeDataErasureRequested>
{
    public async Task HandleAsync(EmployeeDataErasureRequested message, CancellationToken cancellationToken)
    {
        var id = message.EmployeeId;
        await db.InApp.Where(n => n.EmployeeId == id).ExecuteDeleteAsync(cancellationToken);
        await db.Deliveries.Where(d => d.EmployeeId == id).ExecuteDeleteAsync(cancellationToken);
        await db.Preferences.Where(p => p.EmployeeId == id).ExecuteDeleteAsync(cancellationToken);
        await db.EmployeeNames.Where(n => n.EmployeeId == id).ExecuteDeleteAsync(cancellationToken);
        await db.TemplateOverrides.Where(o => o.ChangedBy == id).ExecuteUpdateAsync(s => s.SetProperty(o => o.ChangedBy, Guid.Empty), cancellationToken);
        await permissions.EraseAsync(id, cancellationToken);
    }
}
