namespace Portal.Core.Logbook;

/// <summary>A reply is meant for the level below (regional leader to the store manager, district leader to the regional leaders); an internal comment is only for leaders.</summary>
public enum LogbookCommentKind
{
    Unknown = 0,
    Reply = 1,
    Internal = 2,
}

public enum LogbookCommentError
{
    None = 0,
    TextMissing = 1,
    TextTooLong = 2,
}

/// <summary>A comment on a report. <see cref="Level"/> is the level the author wrote it at (regional or district leader), kept as it was then.</summary>
public sealed record LogbookComment(Guid Id, LogbookCommentKind Kind, LogbookLevel Level, Guid AuthorId, string Text, DateTimeOffset CreatedAt);

/// <summary>
/// Who sees which comment (decision 49). The store manager sees the replies of the regional leaders and no internal comments (the regional leader passes on
/// what the district leader answered). A regional leader sees all replies of regional and district leaders and only the own internal comments. A district
/// leader sees everything. Being store manager and leader at the same time adds up what both see, but never gives internal comments of others.
/// </summary>
public static class LogbookCommentRules
{
    public static bool CanSee(LogbookComment comment, Guid viewer, LogbookLevel viewerLevel, bool storeManager)
    {
        if (viewerLevel == LogbookLevel.District)
        {
            return true;
        }

        if (comment.Kind == LogbookCommentKind.Internal)
        {
            return viewerLevel == LogbookLevel.Regional && comment.AuthorId == viewer && comment.Level == LogbookLevel.Regional;
        }

        if (comment.Kind != LogbookCommentKind.Reply)
        {
            return false;
        }

        return comment.Level == LogbookLevel.Regional ? storeManager || viewerLevel >= LogbookLevel.Regional : viewerLevel >= LogbookLevel.Regional;
    }

    public static IReadOnlyList<LogbookComment> VisibleTo(IEnumerable<LogbookComment> comments, Guid viewer, LogbookLevel viewerLevel, bool storeManager) =>
        [.. comments.Where(c => CanSee(c, viewer, viewerLevel, storeManager))];

    /// <summary>Only regional and district leaders write comments (and only on reports of their stores).</summary>
    public static bool CanWrite(LogbookLevel level) => level >= LogbookLevel.Regional;

    public static LogbookCommentError TextProblem(string? text) =>
        string.IsNullOrWhiteSpace(text) ? LogbookCommentError.TextMissing : text.Length > LogbookLimits.MaxText ? LogbookCommentError.TextTooLong : LogbookCommentError.None;

    /// <summary>"Changed since my reply": the report was revised after the last reply of the viewer. Without a reply of the viewer there is nothing it could be "since".</summary>
    public static bool RevisedSinceReply(DateTimeOffset? revisedAt, DateTimeOffset? lastReplyAt) => revisedAt is { } revised && lastReplyAt is { } reply && revised > reply;
}

/// <summary>The state of a handed-in report: who may set which, and what a reply does.</summary>
public static class LogbookStateRules
{
    /// <summary>A reply of a regional leader puts the report in progress (again, also after "done" or "followed up"); everything else leaves the state alone.</summary>
    public static LogbookReportState AfterComment(LogbookReportState state, LogbookCommentKind kind, LogbookLevel authorLevel) =>
        kind == LogbookCommentKind.Reply && authorLevel == LogbookLevel.Regional ? LogbookReportState.InProgress : state;

    /// <summary>A leader sets "in progress" or "done" (also to reopen); "new" is the start and "followed up" is the store manager's mark.</summary>
    public static bool CanSet(LogbookReportState from, LogbookReportState to) =>
        from != LogbookReportState.Unknown && to is LogbookReportState.InProgress or LogbookReportState.Done;

    /// <summary>The store manager marks a report as followed up as long as it is not done (marking again updates the note).</summary>
    public static bool CanFollowUp(LogbookReportState state) => state != LogbookReportState.Done && state != LogbookReportState.Unknown;

    /// <summary>For the list of the leaders: followed up first (somebody did something), then new, then in progress, then done.</summary>
    public static int Priority(LogbookReportState state) => state switch
    {
        LogbookReportState.FollowedUp => 0,
        LogbookReportState.New => 1,
        LogbookReportState.InProgress => 2,
        _ => 3,
    };
}
