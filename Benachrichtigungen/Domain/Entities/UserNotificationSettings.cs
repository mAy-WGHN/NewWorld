using Benachrichtigungen.Domain.Enums;

namespace Benachrichtigungen.Domain.Entities;

/// <summary>
/// Was ein User an Meldungs-Benachrichtigungen bekommen möchte (Übernahme der bisherigen Felder aus dem Portal-User). Keine Zeile = Standardwerte.
/// </summary>
public class UserNotificationSettings
{
    /// <summary>ID des Users (Primärschlüssel); der User gehört dem Dienst "Rollen-Rechte".</summary>
    public Guid UserId { get; set; }

    public bool BugNotificationsEnabled { get; set; } = true;
    public ReportSeverity BugNotificationMinSeverity { get; set; } = ReportSeverity.High;
    public bool TheftNotificationsEnabled { get; set; }
    public bool OtherNotificationsEnabled { get; set; }
    public bool SuggestionNotificationsEnabled { get; set; }

    /// <summary>ISO-Wochentage (1 = Mo ... 7 = So), an denen irgendein Push zugestellt wird.</summary>
    public List<int> NotificationWeekdays { get; private set; } = [1, 2, 3, 4, 5, 6, 7];

    public void SetNotificationWeekdays(IEnumerable<int> weekdays) =>
        NotificationWeekdays = weekdays.Distinct().ToList();

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
