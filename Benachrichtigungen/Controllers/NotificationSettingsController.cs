using Benachrichtigungen.Authentication;
using Benachrichtigungen.Common.Api;
using Benachrichtigungen.Contracts;
using Benachrichtigungen.Data;
using Benachrichtigungen.Domain.Entities;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Benachrichtigungen.Controllers;

/// <summary>
/// Die eigenen Benachrichtigungs-Einstellungen des angemeldeten Users (Kategorien, Mindest-Schwere bei Bugs, Wochentage). Keine Zeile = Standardwerte.
/// Das Einstellen für andere User (bisher im Portal über die User-Verwaltung) kommt, sobald die Rechteprüfung dieses Dienstes bei "Rollen-Rechte" nachfragt.
/// </summary>
[Route("api/me/notification-settings")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class NotificationSettingsController(NotificationsDbContext db) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var settings = await db.UserNotificationSettings.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId, ct)
                       ?? new UserNotificationSettings { UserId = userId };
        return Success(NotificationSettingsResponse.From(settings));
    }

    [HttpPatch]
    public async Task<IActionResult> Update(UpdateNotificationSettingsRequest request, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var settings = await db.UserNotificationSettings.FirstOrDefaultAsync(s => s.UserId == userId, ct);
        if (settings is null)
        {
            settings = new UserNotificationSettings { UserId = userId };
            db.UserNotificationSettings.Add(settings);
        }

        if (request.BugNotificationsEnabled is not null) settings.BugNotificationsEnabled = request.BugNotificationsEnabled.Value;
        if (request.BugNotificationMinSeverity is not null) settings.BugNotificationMinSeverity = request.BugNotificationMinSeverity.Value;
        if (request.TheftNotificationsEnabled is not null) settings.TheftNotificationsEnabled = request.TheftNotificationsEnabled.Value;
        if (request.OtherNotificationsEnabled is not null) settings.OtherNotificationsEnabled = request.OtherNotificationsEnabled.Value;
        if (request.SuggestionNotificationsEnabled is not null) settings.SuggestionNotificationsEnabled = request.SuggestionNotificationsEnabled.Value;
        if (request.NotificationWeekdays is not null) settings.SetNotificationWeekdays(request.NotificationWeekdays);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            // Parallel angelegt: der andere Aufruf war schneller; die Änderung gilt beim nächsten Aufruf.
            return StatusCode(StatusCodes.Status409Conflict, new ErrorResponse("Die Einstellungen wurden gleichzeitig geändert. Bitte erneut versuchen.", []));
        }

        return Success(NotificationSettingsResponse.From(settings));
    }
}
