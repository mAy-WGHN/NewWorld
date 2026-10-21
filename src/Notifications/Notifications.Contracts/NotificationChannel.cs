using System.Text.Json.Serialization;

namespace Notifications.Contracts;

/// <summary>Delivery channel.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<NotificationChannel>))]
public enum NotificationChannel
{
    /// <summary>Not set.</summary>
    Unknown = 0,

    /// <summary>Stored in the portal ("meine Benachrichtigungen"); the only channel that may carry class 2 data.</summary>
    InApp = 1,

    /// <summary>Web push with generic text.</summary>
    Push = 2,

    /// <summary>E-mail with generic subject.</summary>
    Email = 3,
}
