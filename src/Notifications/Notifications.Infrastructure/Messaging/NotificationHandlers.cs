using BcIntegration.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Notifications.Contracts;
using Notifications.Core.Delivery;
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

/// <summary>Deletes everything about a person: notifications, delivery records, preferences and the permission copy.</summary>
public sealed class EmployeeDataErasureRequestedHandler(NotificationsDbContext db, PermissionsChangedHandler permissions) : IMessageHandler<EmployeeDataErasureRequested>
{
    public async Task HandleAsync(EmployeeDataErasureRequested message, CancellationToken cancellationToken)
    {
        var id = message.EmployeeId;
        await db.InApp.Where(n => n.EmployeeId == id).ExecuteDeleteAsync(cancellationToken);
        await db.Deliveries.Where(d => d.EmployeeId == id).ExecuteDeleteAsync(cancellationToken);
        await db.Preferences.Where(p => p.EmployeeId == id).ExecuteDeleteAsync(cancellationToken);
        await permissions.EraseAsync(id, cancellationToken);
    }
}
