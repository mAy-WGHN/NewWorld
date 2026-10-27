namespace Notifications.Core;

/// <summary>
/// Privileges of the Notifications API, registered with Access. All concern the signed-in person's own data and are not scopable;
/// they belong into the public user role (or other roles) by manual administration in Access.
/// </summary>
public static class NotificationPrivileges
{
    public const string AppKey = "notification";

    public const string ReadOwn = "notification:read-own";
    public const string UpdateOwn = "notification:update-own";
    public const string PreferenceReadOwn = "preference:read-own";
    public const string PreferenceUpdateOwn = "preference:update-own";

    /// <summary>The privileges with what they allow in plain words (German, shown in Access when roles are put together).</summary>
    public static readonly IReadOnlyList<(string Key, string Description)> Catalog =
    [
        (ReadOwn, "Die eigenen Benachrichtigungen im Portal lesen."),
        (UpdateOwn, "Eigene Benachrichtigungen als gelesen markieren oder löschen."),
        (PreferenceReadOwn, "Die eigenen Benachrichtigungseinstellungen ansehen."),
        (PreferenceUpdateOwn, "Die eigenen Benachrichtigungseinstellungen ändern (Pflichtbenachrichtigungen bleiben aktiv)."),
    ];

    public static readonly IReadOnlyList<string> All = [.. Catalog.Select(c => c.Key)];
}
