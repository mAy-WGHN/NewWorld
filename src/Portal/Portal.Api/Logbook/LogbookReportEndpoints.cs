using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Portal.Api.Http;
using Portal.Core;
using Portal.Core.Logbook;
using Portal.Infrastructure;
using Portal.Infrastructure.Logbook;
using Shared.Authorization;

namespace Portal.Api.Logbook;

/// <summary>The answer to one section: the chosen options or the text.</summary>
public sealed record LogbookAnswerDto(List<Guid>? OptionIds, string? Text);

public sealed record SaveLogbookReportRequest(Dictionary<Guid, LogbookAnswerDto>? Answers);

public sealed record LogbookReportDto(
    Guid Id, Guid StoreId, string Week, int TemplateVersion, string Phase, string State, Dictionary<Guid, LogbookAnswerDto> Answers,
    DateTimeOffset? SubmittedAt, string? SubmittedByName, DateTimeOffset? RevisedAt, string? RevisedByName);

public sealed record LogbookStoreWeekDto(Guid StoreId, string StoreNumber, string StoreName, int TemplateVersion, bool CanSubmit, LogbookReportDto? Report);

public sealed record LogbookMyReportsDto(string Week, IReadOnlyList<LogbookStoreWeekDto> Stores);

public sealed record LogbookReportSummaryDto(Guid Id, string Week, string Phase, string State, DateTimeOffset? SubmittedAt, DateTimeOffset? RevisedAt);

/// <summary>
/// The weekly report of the store manager (decision 49): write a draft, hand it in, revise it afterwards. Everybody who holds
/// <see cref="PortalPrivileges.LogbookWrite"/> for the store works on the same report (one per store and week). The week is the Monday in local time;
/// any day of a week means that week. What the regional and district leaders see comes with the next step.
/// </summary>
public static class LogbookReportEndpoints
{
    private const int MaxHistory = 52;

    public static WebApplication MapLogbookReportEndpoints(this WebApplication app)
    {
        var write = AppPolicies.Privilege(PortalPrivileges.LogbookWrite);

        // The stores the person writes for, with the report of the week (default: the current one).
        app.MapGet("/api/logbook/my-reports", async (DateOnly? week, ClaimsPrincipal user, IPermissionCache cache, LogbookReportService service, LogbookStructureStore structures, PortalDbContext db, CancellationToken ct) =>
            {
                var day = week ?? service.CurrentWeek;
                var reach = WriteReach(user, cache);
                var structure = await structures.LoadAsync(ct);
                List<Guid> ids = reach?.Unrestricted == true ? [.. structure.Stores.Select(s => s.StoreId)] : reach is null ? [] : [.. reach.StoreIds];
                var stores = await db.Stores.AsNoTracking().Where(s => ids.Contains(s.StoreId)).OrderBy(s => s.StoreNumber).ToListAsync(ct);
                var weeks = (await service.WeekOfStoresAsync([.. stores.Select(s => s.StoreId)], day, ct)).ToDictionary(w => w.StoreId);
                var names = await PeopleAsync(db, weeks.Values.Select(w => w.Report).Where(r => r is not null).Select(r => r!), ct);
                return Answer.Ok(new LogbookMyReportsDto(
                    LogbookWeek.StartOf(day).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                    [.. stores.Select(s => new LogbookStoreWeekDto(
                        s.StoreId, s.StoreNumber, s.Name, weeks[s.StoreId].TemplateVersion, weeks[s.StoreId].CanSubmit,
                        weeks[s.StoreId].Report is { } report ? ToDto(report, names) : null))]));
            })
            .RequireAuthorization(write);

        app.MapPut("/api/logbook/reports/{storeId:guid}/{week}", async (
                Guid storeId, DateOnly week, SaveLogbookReportRequest request, ClaimsPrincipal user, IPermissionCache cache, LogbookReportService service, PortalDbContext db, CancellationToken ct) =>
            {
                await EnsureStoreAsync(storeId, user, cache, db, ct);
                if (request.Answers is null)
                {
                    throw ApiException.Validation("answers", Validator.Required);
                }

                var result = await service.SaveAsync(storeId, week, request.Answers.ToDictionary(a => a.Key, a => ToAnswer(a.Value)), Me(user), ct);
                return await AnswerAsync(result, db, ct);
            })
            .RequireAuthorization(write);

        app.MapPost("/api/logbook/reports/{storeId:guid}/{week}/submit", async (
                Guid storeId, DateOnly week, ClaimsPrincipal user, IPermissionCache cache, LogbookReportService service, PortalDbContext db, CancellationToken ct) =>
            {
                await EnsureStoreAsync(storeId, user, cache, db, ct);
                return await AnswerAsync(await service.SubmitAsync(storeId, week, Me(user), ct), db, ct);
            })
            .RequireAuthorization(write);

        // The weeks of one store, newest first, for the store manager.
        app.MapGet("/api/logbook/stores/{storeId:guid}/reports", async (
                Guid storeId, int? take, ClaimsPrincipal user, IPermissionCache cache, LogbookReportService service, PortalDbContext db, CancellationToken ct) =>
            {
                await EnsureStoreAsync(storeId, user, cache, db, ct);
                var reports = await service.HistoryAsync(storeId, Math.Clamp(take ?? 12, 1, MaxHistory), ct);
                return Answer.Ok(reports.Select(r => new LogbookReportSummaryDto(r.Id, Format(r.Week), Phase(r.Phase), State(r.State), r.SubmittedAt, r.RevisedAt)));
            })
            .RequireAuthorization(write);

        return app;
    }

    private static Guid Me(ClaimsPrincipal user) => Guid.TryParse(user.FindFirst(AppClaimTypes.EmployeeId)?.Value, out var id) ? id : Guid.Empty;

    private static PrivilegeReach? WriteReach(ClaimsPrincipal user, IPermissionCache cache) =>
        Me(user) is var me && me != Guid.Empty && cache.TryGet(me, out var snapshot) && snapshot is not null && snapshot.Privileges.TryGetValue(PortalPrivileges.LogbookWrite, out var reach)
            ? reach
            : null;

    /// <summary>The store must be in the reach of the write privilege (403), and exist (404).</summary>
    private static async Task EnsureStoreAsync(Guid storeId, ClaimsPrincipal user, IPermissionCache cache, PortalDbContext db, CancellationToken ct)
    {
        var reach = WriteReach(user, cache);
        if (reach is null || (!reach.Unrestricted && !reach.StoreIds.Contains(storeId)))
        {
            throw new ApiException(StatusCodes.Status403Forbidden, EnvelopeAuthorizationResultHandler.Message("update"));
        }

        if (!await db.Stores.AnyAsync(s => s.StoreId == storeId, ct))
        {
            throw ApiException.NotFound();
        }
    }

    private static async Task<IResult> AnswerAsync(LogbookReportResult result, PortalDbContext db, CancellationToken ct)
    {
        switch (result.Failure)
        {
            case LogbookReportFailure.FutureWeek:
                throw ApiException.Validation("week", "Für eine Woche in der Zukunft kann kein Bericht geschrieben werden.");
            case LogbookReportFailure.WeekTooOld:
                throw ApiException.Validation("week", $"Für Wochen, die mehr als {LogbookWeek.MaxWeeksBack} Wochen zurückliegen, kann kein Bericht mehr geschrieben werden.");
            case LogbookReportFailure.AnswersInvalid:
                throw ApiException.Validation([.. (result.Problems ?? []).Select(p => new ApiError("answers", MessageOf(p.Code))).DistinctBy(e => e.Message)]);
            case LogbookReportFailure.NoRecipients:
                throw ApiException.Conflict("Für diese Filiale ist niemand als Regional- oder Bezirksleitung eingetragen. Der Bericht kann erst abgegeben werden, wenn die Zuordnung gepflegt ist.");
            case LogbookReportFailure.Conflict:
                throw ApiException.Conflict("Der Bericht wurde gleichzeitig von jemand anderem angelegt. Bitte neu laden und noch einmal speichern.");
            default:
                return Answer.Ok(ToDto(result.Report!, await PeopleAsync(db, [result.Report!], ct)));
        }
    }

    private static async Task<Dictionary<Guid, string>> PeopleAsync(PortalDbContext db, IEnumerable<LogbookReportRecord> reports, CancellationToken ct)
    {
        var ids = reports.SelectMany(r => new[] { r.SubmittedBy, r.RevisedBy }).Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
        return await db.People.AsNoTracking().Where(p => ids.Contains(p.EmployeeId)).ToDictionaryAsync(p => p.EmployeeId, p => p.Name, ct);
    }

    private static LogbookAnswer ToAnswer(LogbookAnswerDto dto) => new([.. dto.OptionIds ?? []], dto.Text);

    private static LogbookReportDto ToDto(LogbookReportRecord r, IReadOnlyDictionary<Guid, string> names) => new(
        r.Id, r.StoreId, Format(r.Week), r.TemplateVersion, Phase(r.Phase), State(r.State),
        LogbookReportService.ReadAnswers(r).ToDictionary(a => a.Key, a => new LogbookAnswerDto([.. a.Value.OptionIds], a.Value.Text)),
        r.SubmittedAt, Name(names, r.SubmittedBy), r.RevisedAt, Name(names, r.RevisedBy));

    private static string? Name(IReadOnlyDictionary<Guid, string> names, Guid? id) => id is { } value ? names.GetValueOrDefault(value, "(unbekannt)") : null;

    private static string Format(DateOnly date) => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static string Phase(LogbookReportPhase phase) => phase == LogbookReportPhase.Submitted ? "submitted" : "draft";

    private static string State(LogbookReportState state) => state switch
    {
        LogbookReportState.InProgress => "inProgress",
        LogbookReportState.FollowedUp => "followedUp",
        LogbookReportState.Done => "done",
        _ => "new",
    };

    private static string MessageOf(LogbookAnswerError error) => error switch
    {
        LogbookAnswerError.UnknownSection => "Eine Antwort gehört zu keinem Abschnitt der Vorlage.",
        LogbookAnswerError.UnknownOption => "Eine gewählte Möglichkeit gibt es in diesem Abschnitt nicht.",
        LogbookAnswerError.TooManySelections => "In einer Einzelauswahl ist nur eine Möglichkeit erlaubt.",
        LogbookAnswerError.OptionChosenTwice => "Eine Möglichkeit wurde doppelt gewählt.",
        LogbookAnswerError.TextNotAllowed => "Eine Auswahl nimmt keinen Text auf.",
        LogbookAnswerError.OptionsNotAllowed => "Ein Freitext-Abschnitt nimmt keine Auswahl auf.",
        LogbookAnswerError.TextTooLong => $"Ein Text darf höchstens {LogbookLimits.MaxText} Zeichen haben.",
        LogbookAnswerError.RequiredMissing => "Mindestens ein Pflichtabschnitt ist nicht beantwortet.",
        _ => "Die Antworten sind nicht gültig.",
    };
}
