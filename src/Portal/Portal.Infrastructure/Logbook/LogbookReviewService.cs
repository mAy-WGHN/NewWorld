using Microsoft.EntityFrameworkCore;
using Portal.Core.Logbook;

namespace Portal.Infrastructure.Logbook;

/// <summary>A comment on a weekly report (class 2: it can name problems with people; never on the bus, never in a log or a notification text).</summary>
public sealed class LogbookCommentRecord
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The order of writing (several comments can have the same time).</summary>
    public int Sequence { get; set; }

    public Guid ReportId { get; set; }
    public LogbookCommentKind Kind { get; set; }

    /// <summary>The level the author wrote at, kept as it was then.</summary>
    public LogbookLevel Level { get; set; }

    public Guid AuthorId { get; set; }
    public required string Text { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public enum LogbookReviewFailure
{
    None = 0,
    NotFound = 1,
    TextMissing = 2,
    TextTooLong = 3,
    StateNotAllowed = 4,

    /// <summary>The action does not fit the report as it is (for example a follow up of a report that is done or still a draft).</summary>
    Conflict = 5,
}

public sealed record LogbookReviewResult<T>(LogbookReviewFailure Failure, T? Value = default)
{
    public bool Succeeded => Failure == LogbookReviewFailure.None;
}

/// <summary>What a viewer is to a report: the level of a leader for its store, and whether the viewer is a store manager of it.</summary>
public sealed record LogbookViewer(Guid Id, LogbookLevel Level, bool StoreManager);

/// <summary>A report with the comments the viewer may see and what is needed to show it.</summary>
public sealed record LogbookReviewView(LogbookReportRecord Report, IReadOnlyList<LogbookCommentRecord> Comments, bool RevisedSinceMyReply);

/// <summary>A line of the list of the leaders.</summary>
public sealed record LogbookReviewItem(LogbookReportRecord Report, int CommentCount, bool RevisedSinceMyReply);

/// <summary>What the leaders ask for in the list (all optional).</summary>
public sealed record LogbookReviewFilter(DateOnly? Week, Guid? StoreId, Guid? RegionId, LogbookReportState? State, bool OpenOnly);

/// <summary>
/// Reading, commenting and following up the weekly reports (decision 49). The caller says who the viewer is for a report (level and store manager); this
/// service applies the rules: leaders only see handed-in reports of their stores, comments by the visibility matrix, a reply of a regional leader puts the
/// report in progress, the store manager marks it as followed up with a note, and "revised since my reply" compares the last revision with the last reply of
/// the viewer.
/// </summary>
public sealed class LogbookReviewService(PortalDbContext db, LogbookStructureStore structures, TimeProvider time, LogbookReportService reports)
{
    public const int MaxNote = 1000;

    public async Task<LogbookStructure> StructureAsync(CancellationToken cancellationToken) => await structures.LoadAsync(cancellationToken);

    /// <summary>The store of a report, or <c>null</c> if there is no such report.</summary>
    public async Task<Guid?> StoreOfAsync(Guid reportId, CancellationToken cancellationToken) =>
        await db.LogbookReports.AsNoTracking().Where(r => r.Id == reportId).Select(r => (Guid?)r.StoreId).FirstOrDefaultAsync(cancellationToken);

    /// <summary>The handed-in reports of the stores the person leads, filtered, with the followed-up first.</summary>
    public async Task<IReadOnlyList<LogbookReviewItem>> ListAsync(Guid person, LogbookReviewFilter filter, CancellationToken cancellationToken)
    {
        var structure = await structures.LoadAsync(cancellationToken);
        var stores = LogbookReach.Of(structure, person).StoreIds.AsEnumerable();
        if (filter.StoreId is { } storeId)
        {
            stores = stores.Where(s => s == storeId);
        }

        if (filter.RegionId is { } regionId)
        {
            var inRegion = structure.Stores.Where(s => s.RegionId == regionId).Select(s => s.StoreId).ToHashSet();
            stores = stores.Where(inRegion.Contains);
        }

        var ids = stores.ToList();
        var query = db.LogbookReports.AsNoTracking().Where(r => r.Phase == LogbookReportPhase.Submitted && ids.Contains(r.StoreId));
        if (filter.Week is { } week)
        {
            var monday = LogbookWeek.StartOf(week);
            query = query.Where(r => r.Week == monday);
        }

        if (filter.State is { } state)
        {
            query = query.Where(r => r.State == state);
        }

        if (filter.OpenOnly)
        {
            query = query.Where(r => r.State != LogbookReportState.Done);
        }

        var found = await query.ToListAsync(cancellationToken);
        var reportIds = found.Select(r => r.Id).ToList();
        var comments = await db.LogbookComments.AsNoTracking().Where(c => reportIds.Contains(c.ReportId)).ToListAsync(cancellationToken);
        var numbers = await db.Stores.AsNoTracking().Where(s => ids.Contains(s.StoreId)).ToDictionaryAsync(s => s.StoreId, s => s.StoreNumber, cancellationToken);
        return [.. found
            .OrderBy(r => LogbookStateRules.Priority(r.State))
            .ThenByDescending(r => r.Week)
            .ThenBy(r => numbers.GetValueOrDefault(r.StoreId, string.Empty), StringComparer.Ordinal)
            .Select(r =>
            {
                var mine = comments.Where(c => c.ReportId == r.Id).ToList();
                var visible = mine.Count(c => LogbookCommentRules.CanSee(ToComment(c), person, LogbookReach.LevelFor(structure, person, r.StoreId), storeManager: false));
                return new LogbookReviewItem(r, visible, RevisedSince(r, mine, person));
            })];
    }

    /// <summary>The report if the viewer may see it: a leader sees handed-in reports of the stores of the reach, a store manager the reports of the own store (a draft too).</summary>
    public async Task<LogbookReviewView?> GetAsync(Guid reportId, LogbookViewer viewer, CancellationToken cancellationToken)
    {
        var report = await db.LogbookReports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == reportId, cancellationToken);
        if (report is null || !MaySee(report, viewer))
        {
            return null;
        }

        var all = await db.LogbookComments.AsNoTracking().Where(c => c.ReportId == reportId).OrderBy(c => c.CreatedAt).ThenBy(c => c.Sequence).ToListAsync(cancellationToken);
        var visible = all.Where(c => LogbookCommentRules.CanSee(ToComment(c), viewer.Id, viewer.Level, viewer.StoreManager)).ToList();
        return new LogbookReviewView(report, visible, RevisedSince(report, all, viewer.Id));
    }

    public async Task<LogbookReviewResult<LogbookCommentRecord>> AddCommentAsync(Guid reportId, LogbookViewer viewer, LogbookCommentKind kind, string? text, CancellationToken cancellationToken)
    {
        var report = await db.LogbookReports.FirstOrDefaultAsync(r => r.Id == reportId, cancellationToken);
        if (report is null || report.Phase != LogbookReportPhase.Submitted || !LogbookCommentRules.CanWrite(viewer.Level))
        {
            return new(LogbookReviewFailure.NotFound);
        }

        switch (LogbookCommentRules.TextProblem(text))
        {
            case LogbookCommentError.TextMissing:
                return new(LogbookReviewFailure.TextMissing);
            case LogbookCommentError.TextTooLong:
                return new(LogbookReviewFailure.TextTooLong);
        }

        var now = time.GetUtcNow();
        var comment = new LogbookCommentRecord { ReportId = reportId, Kind = kind, Level = viewer.Level, AuthorId = viewer.Id, Text = text!.Trim(), CreatedAt = now };
        db.LogbookComments.Add(comment);
        report.State = LogbookStateRules.AfterComment(report.State, kind, viewer.Level);
        await db.SaveChangesAsync(cancellationToken);
        return new(LogbookReviewFailure.None, comment);
    }

    public async Task<LogbookReviewResult<LogbookReportRecord>> SetStateAsync(Guid reportId, LogbookViewer viewer, LogbookReportState state, CancellationToken cancellationToken)
    {
        var report = await db.LogbookReports.FirstOrDefaultAsync(r => r.Id == reportId, cancellationToken);
        if (report is null || report.Phase != LogbookReportPhase.Submitted || !LogbookCommentRules.CanWrite(viewer.Level))
        {
            return new(LogbookReviewFailure.NotFound);
        }

        if (!LogbookStateRules.CanSet(report.State, state))
        {
            return new(LogbookReviewFailure.StateNotAllowed);
        }

        if (report.State != state)
        {
            report.State = state;
            await db.SaveChangesAsync(cancellationToken);
        }

        return new(LogbookReviewFailure.None, report);
    }

    public async Task<LogbookReviewResult<LogbookReportRecord>> FollowUpAsync(Guid reportId, Guid by, string? note, CancellationToken cancellationToken)
    {
        var report = await db.LogbookReports.FirstOrDefaultAsync(r => r.Id == reportId, cancellationToken);
        if (report is null)
        {
            return new(LogbookReviewFailure.NotFound);
        }

        if (string.IsNullOrWhiteSpace(note))
        {
            return new(LogbookReviewFailure.TextMissing);
        }

        if (note.Trim().Length > MaxNote)
        {
            return new(LogbookReviewFailure.TextTooLong);
        }

        if (report.Phase != LogbookReportPhase.Submitted || !LogbookStateRules.CanFollowUp(report.State))
        {
            return new(LogbookReviewFailure.Conflict);
        }

        report.State = LogbookReportState.FollowedUp;
        report.FollowedUpAt = time.GetUtcNow();
        report.FollowedUpBy = by;
        report.FollowUpNote = note.Trim();
        await db.SaveChangesAsync(cancellationToken);
        return new(LogbookReviewFailure.None, report);
    }

    public DateOnly CurrentWeek => reports.CurrentWeek;

    private static bool MaySee(LogbookReportRecord report, LogbookViewer viewer) =>
        viewer.StoreManager || (viewer.Level >= LogbookLevel.Regional && report.Phase == LogbookReportPhase.Submitted);

    private static LogbookComment ToComment(LogbookCommentRecord c) => new(c.Id, c.Kind, c.Level, c.AuthorId, c.Text, c.CreatedAt);

    /// <summary>The last reply of the viewer (any reply, at the level of a leader) against the last revision of the report.</summary>
    private static bool RevisedSince(LogbookReportRecord report, IEnumerable<LogbookCommentRecord> comments, Guid viewer) =>
        LogbookCommentRules.RevisedSinceReply(
            report.RevisedAt,
            comments.Where(c => c.Kind == LogbookCommentKind.Reply && c.AuthorId == viewer).Select(c => (DateTimeOffset?)c.CreatedAt).Max());
}
