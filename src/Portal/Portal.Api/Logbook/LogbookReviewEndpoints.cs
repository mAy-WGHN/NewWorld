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

/// <summary><c>Kind</c> is <c>reply</c> or <c>internal</c>; <c>Level</c> is <c>regional</c> or <c>district</c> (the level it was written at); <c>Own</c> is true for the viewer's own comments.</summary>
public sealed record LogbookCommentDto(Guid Id, string Kind, string Level, Guid AuthorId, string AuthorName, string Text, DateTimeOffset CreatedAt, bool Own);

public sealed record AddLogbookCommentRequest(string? Kind, string? Text);

public sealed record SetLogbookStateRequest(string? State);

public sealed record FollowUpLogbookRequest(string? Note);

/// <summary>One line of the list of the leaders; <c>RevisedSinceMyReply</c> is true if the store manager changed the report after the viewer's last reply.</summary>
public sealed record LogbookReviewItemDto(
    Guid Id, Guid StoreId, string StoreNumber, string StoreName, string Week, string State, DateTimeOffset? SubmittedAt, DateTimeOffset? RevisedAt,
    DateTimeOffset? FollowedUpAt, bool RevisedSinceMyReply, int CommentCount);

/// <summary><c>ViewerLevel</c> is <c>none</c> (store manager), <c>regional</c> or <c>district</c>. The comments are the ones the viewer may see.</summary>
public sealed record LogbookReportDetailDto(
    Guid Id, Guid StoreId, string StoreNumber, string StoreName, string Week, int TemplateVersion, string Phase, string State, Dictionary<Guid, LogbookAnswerDto> Answers,
    DateTimeOffset? SubmittedAt, string? SubmittedByName, DateTimeOffset? RevisedAt, string? RevisedByName,
    DateTimeOffset? FollowedUpAt, string? FollowedUpByName, string? FollowUpNote, bool RevisedSinceMyReply, string ViewerLevel, IReadOnlyList<LogbookCommentDto> Comments);

/// <summary>
/// The regional and district leaders at the weekly reports (decision 49): read the handed-in reports of the stores of their reach, reply and comment
/// internally (visible by the matrix of <see cref="LogbookCommentRules"/>), set the state, and see that the store manager followed up. A report outside the
/// reach, and a draft for a leader, is "not found". The store manager reads the own reports with the replies meant for them.
/// </summary>
public static class LogbookReviewEndpoints
{
    public static WebApplication MapLogbookReviewEndpoints(this WebApplication app)
    {
        var review = AppPolicies.Privilege(PortalPrivileges.LogbookReview);

        app.MapGet("/api/logbook/reports", async (
                DateOnly? week, Guid? storeId, Guid? regionId, string? state, bool? open, ClaimsPrincipal user, LogbookReviewService service, PortalDbContext db, CancellationToken ct) =>
            {
                LogbookReportState? parsed = null;
                if (!string.IsNullOrWhiteSpace(state))
                {
                    parsed = ParseState(state) ?? throw ApiException.Validation("state", "Der Stand muss new, inProgress, followedUp oder done sein.");
                }

                // Without a week the current one, unless the open ones of all weeks are asked for.
                var openOnly = open == true;
                var weekFilter = week ?? (openOnly ? null : service.CurrentWeek);
                var items = await service.ListAsync(Me(user), new LogbookReviewFilter(weekFilter, storeId, regionId, parsed, openOnly), ct);
                var stores = await db.Stores.AsNoTracking().Where(s => items.Select(i => i.Report.StoreId).Contains(s.StoreId)).ToDictionaryAsync(s => s.StoreId, ct);
                return Answer.Ok(items.Select(i => new LogbookReviewItemDto(
                    i.Report.Id, i.Report.StoreId, stores.GetValueOrDefault(i.Report.StoreId)?.StoreNumber ?? string.Empty, stores.GetValueOrDefault(i.Report.StoreId)?.Name ?? "(unbekannt)",
                    LogbookReportEndpoints.Format(i.Report.Week), LogbookReportEndpoints.State(i.Report.State), i.Report.SubmittedAt, i.Report.RevisedAt, i.Report.FollowedUpAt,
                    i.RevisedSinceMyReply, i.CommentCount)));
            })
            .RequireAuthorization(review);

        app.MapGet("/api/logbook/reports/{id:guid}", async (Guid id, ClaimsPrincipal user, IPermissionCache cache, LogbookReviewService service, PortalDbContext db, CancellationToken ct) =>
            {
                var viewer = await ViewerAsync(id, user, cache, service, ct) ?? throw ApiException.NotFound();
                var view = await service.GetAsync(id, viewer, ct) ?? throw ApiException.NotFound();
                return Answer.Ok(await ToDetailAsync(view, viewer, db, ct));
            })
            .RequireAuthorization();

        app.MapPost("/api/logbook/reports/{id:guid}/comments", async (
                Guid id, AddLogbookCommentRequest request, ClaimsPrincipal user, IPermissionCache cache, LogbookReviewService service, PortalDbContext db, CancellationToken ct) =>
            {
                var kind = request.Kind?.Trim().ToLowerInvariant() switch
                {
                    "reply" => LogbookCommentKind.Reply,
                    "internal" => LogbookCommentKind.Internal,
                    _ => LogbookCommentKind.Unknown,
                };
                if (kind == LogbookCommentKind.Unknown)
                {
                    throw ApiException.Validation("kind", "Die Art muss reply oder internal sein.");
                }

                var viewer = await ViewerAsync(id, user, cache, service, ct) ?? throw ApiException.NotFound();
                var result = await service.AddCommentAsync(id, viewer, kind, request.Text, ct);
                Check(result.Failure, "text");
                var names = await db.People.AsNoTracking().Where(p => p.EmployeeId == viewer.Id).ToDictionaryAsync(p => p.EmployeeId, p => p.Name, ct);
                return Answer.Ok(ToDto(result.Value!, viewer.Id, names));
            })
            .RequireAuthorization(review);

        app.MapPut("/api/logbook/reports/{id:guid}/state", async (Guid id, SetLogbookStateRequest request, ClaimsPrincipal user, IPermissionCache cache, LogbookReviewService service, PortalDbContext db, CancellationToken ct) =>
            {
                var state = ParseState(request.State);
                var viewer = await ViewerAsync(id, user, cache, service, ct) ?? throw ApiException.NotFound();
                if (state is null)
                {
                    throw ApiException.Validation("state", "Der Stand muss inProgress oder done sein.");
                }

                var result = await service.SetStateAsync(id, viewer, state.Value, ct);
                Check(result.Failure, "state");
                var view = await service.GetAsync(id, viewer, ct) ?? throw ApiException.NotFound();
                return Answer.Ok(await ToDetailAsync(view, viewer, db, ct));
            })
            .RequireAuthorization(review);

        app.MapPost("/api/logbook/reports/{id:guid}/follow-up", async (
                Guid id, FollowUpLogbookRequest request, ClaimsPrincipal user, IPermissionCache cache, LogbookReviewService service, PortalDbContext db, CancellationToken ct) =>
            {
                if (await service.StoreOfAsync(id, ct) is null)
                {
                    throw ApiException.NotFound();
                }

                var viewer = await ViewerAsync(id, user, cache, service, ct);
                if (viewer is null || !viewer.StoreManager)
                {
                    throw new ApiException(StatusCodes.Status403Forbidden, EnvelopeAuthorizationResultHandler.Message("update"));
                }

                var result = await service.FollowUpAsync(id, viewer.Id, request.Note, ct);
                Check(result.Failure, "note");
                var view = await service.GetAsync(id, viewer, ct) ?? throw ApiException.NotFound();
                return Answer.Ok(await ToDetailAsync(view, viewer, db, ct));
            })
            .RequireAuthorization(AppPolicies.Privilege(PortalPrivileges.LogbookWrite));

        return app;
    }

    private static Guid Me(ClaimsPrincipal user) => Guid.TryParse(user.FindFirst(AppClaimTypes.EmployeeId)?.Value, out var id) ? id : Guid.Empty;

    /// <summary>What the person is to the report: the level of a leader for its store (only with the review privilege) and whether they write for the store.</summary>
    private static async Task<LogbookViewer?> ViewerAsync(Guid reportId, ClaimsPrincipal user, IPermissionCache cache, LogbookReviewService service, CancellationToken ct)
    {
        var me = Me(user);
        if (await service.StoreOfAsync(reportId, ct) is not { } storeId)
        {
            return null;
        }

        var privileges = me != Guid.Empty && cache.TryGet(me, out var snapshot) && snapshot is not null ? snapshot.Privileges : null;
        var level = privileges is not null && privileges.ContainsKey(PortalPrivileges.LogbookReview) ? LogbookReach.LevelFor(await service.StructureAsync(ct), me, storeId) : LogbookLevel.None;
        var manager = privileges is not null && privileges.TryGetValue(PortalPrivileges.LogbookWrite, out var reach) && (reach.Unrestricted || reach.StoreIds.Contains(storeId));
        return level == LogbookLevel.None && !manager ? null : new LogbookViewer(me, level, manager);
    }

    private static void Check(LogbookReviewFailure failure, string textField)
    {
        switch (failure)
        {
            case LogbookReviewFailure.None:
                return;
            case LogbookReviewFailure.NotFound:
                throw ApiException.NotFound();
            case LogbookReviewFailure.TextMissing:
                throw ApiException.Validation(textField, Validator.Required);
            case LogbookReviewFailure.TextTooLong:
                throw ApiException.Validation(textField, "Dieser Text ist zu lang.");
            case LogbookReviewFailure.StateNotAllowed:
                throw ApiException.Validation(textField, "Dieser Stand kann nicht gesetzt werden.");
            default:
                throw ApiException.Conflict("Das ist bei diesem Bericht gerade nicht möglich (er ist abgeschlossen oder noch nicht abgegeben).");
        }
    }

    private static LogbookReportState? ParseState(string? state) => state?.Trim().ToLowerInvariant() switch
    {
        "new" => LogbookReportState.New,
        "inprogress" => LogbookReportState.InProgress,
        "followedup" => LogbookReportState.FollowedUp,
        "done" => LogbookReportState.Done,
        _ => null,
    };

    private static async Task<LogbookReportDetailDto> ToDetailAsync(LogbookReviewView view, LogbookViewer viewer, PortalDbContext db, CancellationToken ct)
    {
        var report = view.Report;
        var people = await LogbookReportEndpoints.PeopleAsync(db, [report], ct);
        var authorIds = view.Comments.Select(c => c.AuthorId).Distinct().ToList();
        foreach (var (id, name) in await db.People.AsNoTracking().Where(p => authorIds.Contains(p.EmployeeId)).ToDictionaryAsync(p => p.EmployeeId, p => p.Name, ct))
        {
            people[id] = name;
        }

        var store = await db.Stores.AsNoTracking().FirstOrDefaultAsync(s => s.StoreId == report.StoreId, ct);
        var basic = LogbookReportEndpoints.ToDto(report, people);
        return new LogbookReportDetailDto(
            basic.Id, basic.StoreId, store?.StoreNumber ?? string.Empty, store?.Name ?? "(unbekannt)", basic.Week, basic.TemplateVersion, basic.Phase, basic.State, basic.Answers,
            basic.SubmittedAt, basic.SubmittedByName, basic.RevisedAt, basic.RevisedByName,
            report.FollowedUpAt, report.FollowedUpBy is { } by ? people.GetValueOrDefault(by, "(unbekannt)") : null, report.FollowUpNote,
            view.RevisedSinceMyReply, viewer.Level.ToString().ToLowerInvariant(), [.. view.Comments.Select(c => ToDto(c, viewer.Id, people))]);
    }

    private static LogbookCommentDto ToDto(LogbookCommentRecord c, Guid viewer, IReadOnlyDictionary<Guid, string> names) => new(
        c.Id, c.Kind == LogbookCommentKind.Reply ? "reply" : "internal", c.Level.ToString().ToLowerInvariant(), c.AuthorId, names.GetValueOrDefault(c.AuthorId, "(unbekannt)"), c.Text,
        c.CreatedAt, c.AuthorId == viewer);
}
