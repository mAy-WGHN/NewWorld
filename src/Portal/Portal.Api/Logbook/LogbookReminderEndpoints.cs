using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Portal.Api.Http;
using Portal.Core;
using Portal.Core.Logbook;
using Portal.Infrastructure;
using Portal.Infrastructure.Logbook;
using Shared.Authorization;

namespace Portal.Api.Logbook;

public sealed record RemindLogbookRequest(Guid? StoreId, DateOnly? Week);

public sealed record LogbookReminderDto(Guid Id, Guid StoreId, string Week, DateTimeOffset SentAt);

public sealed record LogbookMissingStoreDto(
    Guid StoreId, string StoreNumber, string StoreName, int RemindersSent, DateTimeOffset? LastReminderAt, bool HasStoreManagers, bool ReportStarted);

public sealed record LogbookMissingDto(string Week, IReadOnlyList<LogbookMissingStoreDto> Stores);

public sealed record LogbookReminderPointDto(int DaysAfterDue, string Time);

/// <summary><c>DueDay</c> is the English weekday in lower case (<c>friday</c>), times are <c>HH:mm</c> in local time.</summary>
public sealed record LogbookReminderSettingsDto(bool Enabled, DateTimeOffset? EnabledAt, string DueDay, string DueTime, IReadOnlyList<LogbookReminderPointDto> Points, int MaxReminders);

public sealed record SetLogbookReminderSettingsRequest(bool? Enabled, string? DueDay, string? DueTime, List<LogbookReminderPointDto>? Points, int? MaxReminders);

/// <summary>
/// Reminding the store managers of the weekly report (decision 49): the leaders see which stores of their reach have not handed in the report of a week and
/// remind them by hand; the settings of the automatic reminder (on or off, due day and time, points, maximum) are maintained by the holders of
/// <see cref="PortalPrivileges.LogbookManageReminders"/>. The text of the reminder is a notification template that the personnel department maintains.
/// </summary>
public static class LogbookReminderEndpoints
{
    public static WebApplication MapLogbookReminderEndpoints(this WebApplication app)
    {
        var review = AppPolicies.Privilege(PortalPrivileges.LogbookReview);
        var manage = AppPolicies.Privilege(PortalPrivileges.LogbookManageReminders);

        app.MapGet("/api/logbook/missing", async (DateOnly? week, ClaimsPrincipal user, LogbookReminderService service, LogbookReportService reports, PortalDbContext db, CancellationToken ct) =>
            {
                var day = week ?? reports.CurrentWeek;
                var missing = await service.MissingAsync(Me(user), day, ct);
                var ids = missing.Select(m => m.StoreId).ToList();
                var stores = await db.Stores.AsNoTracking().Where(s => ids.Contains(s.StoreId)).ToDictionaryAsync(s => s.StoreId, ct);
                return Answer.Ok(new LogbookMissingDto(
                    Format(LogbookWeek.StartOf(day)),
                    [.. missing
                        .Select(m => new LogbookMissingStoreDto(
                            m.StoreId, stores.GetValueOrDefault(m.StoreId)?.StoreNumber ?? string.Empty, stores.GetValueOrDefault(m.StoreId)?.Name ?? "(unbekannt)",
                            m.RemindersSent, m.LastReminderAt, m.HasStoreManagers, m.ReportStarted))
                        .OrderBy(m => m.StoreNumber, StringComparer.Ordinal)]));
            })
            .RequireAuthorization(review);

        app.MapPost("/api/logbook/reminders", async (RemindLogbookRequest request, ClaimsPrincipal user, LogbookReminderService service, LogbookReportService reports, CancellationToken ct) =>
            {
                if (request.StoreId is not { } storeId || storeId == Guid.Empty)
                {
                    throw ApiException.Validation("storeId", Validator.Required);
                }

                // Only a leader of the store may remind; a store outside the reach is "not found".
                var structure = await service.StructureAsync(ct);
                if (LogbookReach.LevelFor(structure, Me(user), storeId) == LogbookLevel.None)
                {
                    throw ApiException.NotFound();
                }

                var result = await service.RemindAsync(storeId, request.Week ?? reports.CurrentWeek, Me(user), ct);
                return result.Failure switch
                {
                    LogbookReminderFailure.None => Answer.Ok(new LogbookReminderDto(result.Reminder!.Id, result.Reminder.StoreId, Format(result.Reminder.Week), result.Reminder.SentAt)),
                    LogbookReminderFailure.FutureWeek => throw ApiException.Validation("week", "Für eine Woche in der Zukunft gibt es nichts anzumahnen."),
                    LogbookReminderFailure.WeekTooOld => throw ApiException.Validation("week", $"Für Wochen, die mehr als {LogbookWeek.MaxWeeksBack} Wochen zurückliegen, wird nicht mehr erinnert."),
                    LogbookReminderFailure.AlreadySubmitted => throw ApiException.Conflict("Der Bericht dieser Woche ist schon abgegeben."),
                    LogbookReminderFailure.TooSoon => throw ApiException.Conflict($"Es wurde vor kurzem schon erinnert. Eine weitere Erinnerung ist nach {LogbookReminderService.MinManualInterval.TotalHours:0} Stunden möglich."),
                    LogbookReminderFailure.NoStoreManagers => throw ApiException.Conflict("Für diese Filiale ist niemand als Filialleitung eingetragen, die Erinnerung ginge ins Leere."),
                    _ => throw ApiException.NotFound(),
                };
            })
            .RequireAuthorization(review);

        app.MapGet("/api/logbook/reminder-settings", async (LogbookReminderService service, CancellationToken ct) => Answer.Ok(ToDto(await service.GetSettingsAsync(ct))))
            .RequireAuthorization(manage);

        app.MapPut("/api/logbook/reminder-settings", async (SetLogbookReminderSettingsRequest request, ClaimsPrincipal user, LogbookReminderService service, CancellationToken ct) =>
            {
                var settings = Parse(request);
                return Answer.Ok(ToDto(await service.SaveSettingsAsync(settings, Me(user), ct)));
            })
            .RequireAuthorization(manage);

        return app;
    }

    private static Guid Me(ClaimsPrincipal user) => Guid.TryParse(user.FindFirst(AppClaimTypes.EmployeeId)?.Value, out var id) ? id : Guid.Empty;

    private static string Format(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static LogbookReminderSettings Parse(SetLogbookReminderSettingsRequest request)
    {
        var errors = new List<ApiError>();
        if (request.Enabled is null)
        {
            errors.Add(new("enabled", Validator.Required));
        }

        if (request.MaxReminders is null)
        {
            errors.Add(new("maxReminders", Validator.Required));
        }

        if (request.Points is null)
        {
            errors.Add(new("points", Validator.Required));
        }

        DayOfWeek? day = request.DueDay?.Trim().ToLowerInvariant() switch
        {
            "monday" => DayOfWeek.Monday,
            "tuesday" => DayOfWeek.Tuesday,
            "wednesday" => DayOfWeek.Wednesday,
            "thursday" => DayOfWeek.Thursday,
            "friday" => DayOfWeek.Friday,
            "saturday" => DayOfWeek.Saturday,
            "sunday" => DayOfWeek.Sunday,
            _ => null,
        };
        if (day is null)
        {
            errors.Add(new("dueDay", "Der Fälligkeitstag muss ein englischer Wochentag sein, zum Beispiel friday."));
        }

        var dueTime = ParseTime(request.DueTime);
        if (dueTime is null)
        {
            errors.Add(new("dueTime", "Die Uhrzeit muss im Format HH:mm angegeben werden."));
        }

        var points = new List<LogbookReminderPoint>();
        foreach (var point in request.Points ?? [])
        {
            if (ParseTime(point.Time) is not { } time)
            {
                errors.Add(new("points", "Die Uhrzeit eines Zeitpunkts muss im Format HH:mm angegeben werden."));
                continue;
            }

            points.Add(new LogbookReminderPoint(point.DaysAfterDue, time));
        }

        if (errors.Count > 0)
        {
            throw ApiException.Validation([.. errors.DistinctBy(e => (e.Field, e.Message))]);
        }

        var settings = new LogbookReminderSettings(request.Enabled!.Value, null, day!.Value, dueTime!.Value, points, request.MaxReminders!.Value);
        foreach (var error in LogbookReminderValidator.Validate(settings))
        {
            errors.Add(error switch
            {
                LogbookReminderError.TooManyPoints => new("points", $"Es sind höchstens {LogbookReminderValidator.MaxPoints} Zeitpunkte möglich."),
                LogbookReminderError.DaysOutOfRange => new("points", $"Die Tage nach der Fälligkeit müssen zwischen 0 und {LogbookReminderValidator.MaxDays} liegen."),
                LogbookReminderError.DuplicatePoint => new("points", "Ein Zeitpunkt kommt doppelt vor."),
                LogbookReminderError.MaxOutOfRange => new("maxReminders", $"Die Höchstzahl muss zwischen 1 und {LogbookReminderValidator.MaxRemindersLimit} liegen."),
                _ => new("dueDay", "Der Fälligkeitstag ist ungültig."),
            });
        }

        if (errors.Count > 0)
        {
            throw ApiException.Validation(errors);
        }

        return settings;
    }

    private static TimeOnly? ParseTime(string? text) =>
        TimeOnly.TryParseExact(text?.Trim(), ["HH:mm", "H:mm", "HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time : null;

    private static LogbookReminderSettingsDto ToDto(LogbookReminderSettings s) => new(
        s.Enabled, s.EnabledAt, s.DueDay.ToString().ToLowerInvariant(), s.DueTime.ToString("HH:mm", CultureInfo.InvariantCulture),
        [.. LogbookReminderPlan.Ordered(s).Select(p => new LogbookReminderPointDto(p.DaysAfterDue, p.Time.ToString("HH:mm", CultureInfo.InvariantCulture)))], s.MaxReminders);
}
