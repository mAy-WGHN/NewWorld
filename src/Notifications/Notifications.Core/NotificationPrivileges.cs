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

    public static readonly IReadOnlyList<string> All = [ReadOwn, UpdateOwn, PreferenceReadOwn, PreferenceUpdateOwn];
}
