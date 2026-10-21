using System.ComponentModel.DataAnnotations;
using Benachrichtigungen.Domain.Entities;
using Benachrichtigungen.Domain.Enums;

namespace Benachrichtigungen.Contracts;

public record NotificationSettingsResponse(
    bool BugNotificationsEnabled,
    ReportSeverity BugNotificationMinSeverity,
    bool TheftNotificationsEnabled,
    bool OtherNotificationsEnabled,
    bool SuggestionNotificationsEnabled,
    IReadOnlyList<int> NotificationWeekdays)
{
    public static NotificationSettingsResponse From(UserNotificationSettings s) => new(
        s.BugNotificationsEnabled, s.BugNotificationMinSeverity, s.TheftNotificationsEnabled, s.OtherNotificationsEnabled,
        s.SuggestionNotificationsEnabled, s.NotificationWeekdays.ToList());
}

/// <summary>PATCH: fehlend/<c>null</c> = unverändert.</summary>
public record UpdateNotificationSettingsRequest
{
    public bool? BugNotificationsEnabled { get; init; }
    public ReportSeverity? BugNotificationMinSeverity { get; init; }
    public bool? TheftNotificationsEnabled { get; init; }
    public bool? OtherNotificationsEnabled { get; init; }
    public bool? SuggestionNotificationsEnabled { get; init; }

    [Weekdays]
    public List<int>? NotificationWeekdays { get; init; }
}

/// <summary>ISO-Wochentage: jeder Eintrag zwischen 1 (Montag) und 7 (Sonntag).</summary>
[AttributeUsage(AttributeTargets.Property)]
public class WeekdaysAttribute : ValidationAttribute
{
    public WeekdaysAttribute() => ErrorMessage = "Jeder Wochentag muss zwischen 1 und 7 liegen.";

    public override bool IsValid(object? value) =>
        value is null || (value is IEnumerable<int> days && days.All(d => d is >= 1 and <= 7));
}
