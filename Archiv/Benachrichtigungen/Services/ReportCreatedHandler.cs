using Benachrichtigungen.Contracts;
using Benachrichtigungen.Data;
using Benachrichtigungen.Domain.Entities;
using Benachrichtigungen.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Benachrichtigungen.Services;

/// <summary>
/// Ereignis <c>report.created</c> (das Portal hat eine Meldung angelegt): Web-Push an die User, die die Meldung <b>sehen dürfen</b> (Recht <c>report:read</c> für eine der
/// Filialen der Meldung — das beantwortet "Rollen-Rechte"), ein Gerät registriert haben und laut ihren Einstellungen benachrichtigt werden wollen (Kategorie,
/// Mindest-Schwere, Wochentag in Europe/Berlin). Ersetzt den bisherigen <c>ReportNotifier</c> des Portals. Fehler beim Versand werden nur geloggt, nie weitergereicht;
/// ist "Rollen-Rechte" nicht erreichbar, wirft <see cref="RecipientDirectoryUnavailableException"/> (der Absender wiederholt das Ereignis).
/// </summary>
public class ReportCreatedHandler(
    NotificationsDbContext db, IRecipientDirectory directory, IPushNotificationSender sender, IConfiguration configuration, TimeProvider clock,
    ILogger<ReportCreatedHandler> logger)
{
    public const string EventType = "report.created";
    public const string ReadPrivilege = "report:read";

    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    /// <summary>Verarbeitet das Ereignis und liefert die Zahl der benachrichtigten User.</summary>
    public async Task<int> HandleAsync(ReportCreatedData data, CancellationToken ct)
    {
        var isoWeekday = IsoWeekday(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Berlin));

        var candidates = await db.PushSubscriptions.Select(s => s.UserId).Distinct().ToListAsync(ct);
        if (candidates.Count == 0) return 0;

        // Wer die Meldung überhaupt sehen darf: aktive User mit report:read für eine ihrer Filialen (oder uneingeschränkt).
        var allowed = await directory.FindUsersAsync(ReadPrivilege, data.LocationIds ?? [], [data.ReportId!.Value], candidates, ct);
        if (allowed.Count == 0) return 0;

        var settings = await db.UserNotificationSettings.Where(s => allowed.Contains(s.UserId)).ToDictionaryAsync(s => s.UserId, ct);
        var recipientIds = allowed
            .Where(id =>
            {
                var own = settings.GetValueOrDefault(id) ?? new UserNotificationSettings { UserId = id }; // keine Zeile = Standardwerte
                return own.NotificationWeekdays.Contains(isoWeekday) && WantsNotification(own, data.Category!.Value, data.Severity!.Value);
            })
            .ToList();
        if (recipientIds.Count == 0) return 0;

        try
        {
            var subscriptions = await db.PushSubscriptions.Where(s => recipientIds.Contains(s.UserId)).ToListAsync(ct);
            var baseUrl = (configuration["Frontend:BaseUrl"] ?? "").TrimEnd('/');
            var url = $"{baseUrl}/wghn-admin/meldungen/{data.Category.ToString()!.ToLowerInvariant()}";
            await sender.SendToAllAsync(subscriptions, Title(data.Category!.Value, data.Severity!.Value), data.Description ?? "", url, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Benachrichtigung für Meldung {ReportId} fehlgeschlagen", data.ReportId);
        }

        return recipientIds.Count;
    }

    public static bool WantsNotification(UserNotificationSettings settings, ReportCategory category, ReportSeverity severity) => category switch
    {
        // Virus-Meldungen haben keinen eigenen Schalter und lösen immer einen Push aus.
        ReportCategory.Virus => true,
        ReportCategory.Bug => settings.BugNotificationsEnabled && severity.Rank() >= settings.BugNotificationMinSeverity.Rank(),
        ReportCategory.Theft => settings.TheftNotificationsEnabled,
        ReportCategory.Other => settings.OtherNotificationsEnabled,
        ReportCategory.Suggestion => settings.SuggestionNotificationsEnabled,
        _ => false
    };

    public static string Title(ReportCategory category, ReportSeverity severity) => category switch
    {
        ReportCategory.Virus => "Neue Meldung: Virus-Verdacht",
        ReportCategory.Bug => $"Neue Meldung: Bug ({severity.ToString().ToLowerInvariant()})",
        ReportCategory.Theft => "Neue Meldung: Diebstahl",
        ReportCategory.Other => "Neue Meldung: Sonstiger Verstoß",
        _ => "Neuer Verbesserungsvorschlag"
    };

    // ISO: Montag = 1 ... Sonntag = 7.
    private static int IsoWeekday(DateTimeOffset date) =>
        date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek;
}
