namespace Notifications.Core;

/// <summary>
/// Privileges of the Notifications API, registered with Access. All but <see cref="TemplateManage"/> concern the signed-in person's own data; none is
/// scopable. The own ones belong into the public user role, <see cref="TemplateManage"/> to HR (manual administration in Access).
/// </summary>
public static class NotificationPrivileges
{
    public const string AppKey = "notification";

    public const string ReadOwn = "notification:read-own";
    public const string UpdateOwn = "notification:update-own";
    public const string PreferenceReadOwn = "preference:read-own";
    public const string PreferenceUpdateOwn = "preference:update-own";

    /// <summary>Changes the texts of all notifications (HR); not scopable, not an own-data privilege.</summary>
    public const string TemplateManage = "template:manage";

    /// <summary>The privileges with what they allow in plain words (German, shown in Access when roles are put together).</summary>
    public static readonly IReadOnlyList<(string Key, string Description)> Catalog =
    [
        (ReadOwn, "Die eigenen Benachrichtigungen im Portal lesen."),
        (UpdateOwn, "Eigene Benachrichtigungen als gelesen markieren oder löschen."),
        (PreferenceReadOwn, "Die eigenen Benachrichtigungseinstellungen ansehen."),
        (PreferenceUpdateOwn, "Die eigenen Benachrichtigungseinstellungen ändern (Pflichtbenachrichtigungen bleiben aktiv)."),
        (TemplateManage, "Die Texte aller Benachrichtigungen ansehen und ändern (Titel und Text für In-App, Push und Mail, mit Variablen wie $USER.firstName), Vorschau ansehen und auf den Standardtext zurücksetzen (HR). Gilt für alle Vorlagen, nicht nur für die eigenen."),
    ];

    public static readonly IReadOnlyList<string> All = [.. Catalog.Select(c => c.Key)];
}
