using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Portal.Core.Logbook;

namespace Portal.Infrastructure.Logbook;

/// <summary>
/// A weekly report of a store (decision 49). One per store and week (unique), filled in with one version of the template. The answers are class 2 (they
/// can name problems with people): they stay in the Portal, never on the bus, never in a log or a notification text.
/// </summary>
public sealed class LogbookReportRecord
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid StoreId { get; set; }

    /// <summary>The Monday of the week.</summary>
    public DateOnly Week { get; set; }

    public int TemplateVersion { get; set; }
    public LogbookReportPhase Phase { get; set; } = LogbookReportPhase.Draft;
    public LogbookReportState State { get; set; } = LogbookReportState.New;
    public required string AnswersJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public Guid? SubmittedBy { get; set; }

    /// <summary>Set when the answers were changed after the hand in (so the regional leader can see the report was revised).</summary>
    public DateTimeOffset? RevisedAt { get; set; }

    public Guid? RevisedBy { get; set; }
}

public enum LogbookReportFailure
{
    None = 0,
    FutureWeek = 1,
    WeekTooOld = 2,
    AnswersInvalid = 3,

    /// <summary>The store is not in the structure or nobody is to be told: handing in would go nowhere.</summary>
    NoRecipients = 4,

    /// <summary>Somebody created the same report at the same time.</summary>
    Conflict = 5,
}

public sealed record LogbookReportResult(LogbookReportFailure Failure, LogbookReportRecord? Report = null, IReadOnlyList<LogbookAnswerProblem>? Problems = null)
{
    public bool Succeeded => Failure == LogbookReportFailure.None;
}

/// <summary>A store with the report of a week and what is needed to show it.</summary>
public sealed record LogbookStoreWeek(Guid StoreId, LogbookReportRecord? Report, int TemplateVersion, bool CanSubmit);

/// <summary>
/// Writing and handing in the weekly report. The week is the Monday (local time); a report keeps the template version it was started with; a draft may be
/// incomplete, the hand in needs the required sections, and a handed-in report stays changeable (a change is marked as a revision) but keeps its required
/// sections answered.
/// </summary>
public sealed class LogbookReportService(PortalDbContext db, LogbookTemplateStore templates, LogbookStructureStore structures, TimeProvider time, LogbookOptions options)
{
    private static readonly JsonSerializerOptions Json = new();

    public DateOnly CurrentWeek => LogbookWeek.Current(time.GetUtcNow(), options.Zone);

    public static Dictionary<Guid, LogbookAnswer> ReadAnswers(LogbookReportRecord report) =>
        JsonSerializer.Deserialize<Dictionary<Guid, LogbookAnswer>>(report.AnswersJson, Json) ?? [];

    /// <summary>The week check for a day (any day of the week).</summary>
    public LogbookWeekError CheckWeek(DateOnly day) => LogbookWeek.Check(day, CurrentWeek);

    public async Task<LogbookReportResult> SaveAsync(Guid storeId, DateOnly day, IReadOnlyDictionary<Guid, LogbookAnswer> answers, Guid by, CancellationToken cancellationToken)
    {
        if (WeekFailure(day) is { } failure)
        {
            return new(failure);
        }

        var week = LogbookWeek.StartOf(day);
        var report = await db.LogbookReports.FirstOrDefaultAsync(r => r.StoreId == storeId && r.Week == week, cancellationToken);
        var template = report is null ? await templates.CurrentAsync(cancellationToken) : await templates.GetAsync(report.TemplateVersion, cancellationToken) ?? LogbookTemplate.Default;
        var canonical = Canonical(answers);

        var problems = LogbookAnswerValidator.Validate(template, canonical, forSubmit: report?.Phase == LogbookReportPhase.Submitted);
        if (problems.Count > 0)
        {
            return new(LogbookReportFailure.AnswersInvalid, null, problems);
        }

        var now = time.GetUtcNow();
        var serialized = Serialize(canonical);
        if (report is null)
        {
            report = new LogbookReportRecord
            {
                StoreId = storeId, Week = week, TemplateVersion = template.Version, AnswersJson = serialized, CreatedAt = now, CreatedBy = by, UpdatedAt = now,
            };
            db.LogbookReports.Add(report);
        }
        else if (Serialize(Canonical(ReadAnswers(report))) != serialized) // compared as content: the database keeps its own spelling of the JSON
        {
            report.AnswersJson = serialized;
            report.UpdatedAt = now;
            if (report.Phase == LogbookReportPhase.Submitted)
            {
                report.RevisedAt = now;
                report.RevisedBy = by;
            }
        }

        return await SaveChangesAsync(report, cancellationToken);
    }

    public async Task<LogbookReportResult> SubmitAsync(Guid storeId, DateOnly day, Guid by, CancellationToken cancellationToken)
    {
        if (WeekFailure(day) is { } failure)
        {
            return new(failure);
        }

        var week = LogbookWeek.StartOf(day);
        var report = await db.LogbookReports.FirstOrDefaultAsync(r => r.StoreId == storeId && r.Week == week, cancellationToken);
        if (report?.Phase == LogbookReportPhase.Submitted)
        {
            return new(LogbookReportFailure.None, report); // a second click changes nothing
        }

        if (!LogbookReach.CanReceiveReports(await structures.LoadAsync(cancellationToken), storeId))
        {
            return new(LogbookReportFailure.NoRecipients);
        }

        var template = report is null ? await templates.CurrentAsync(cancellationToken) : await templates.GetAsync(report.TemplateVersion, cancellationToken) ?? LogbookTemplate.Default;
        var answers = report is null ? [] : ReadAnswers(report);
        var problems = LogbookAnswerValidator.Validate(template, answers, forSubmit: true);
        if (problems.Count > 0)
        {
            return new(LogbookReportFailure.AnswersInvalid, null, problems);
        }

        var now = time.GetUtcNow();
        if (report is null)
        {
            report = new LogbookReportRecord { StoreId = storeId, Week = week, TemplateVersion = template.Version, AnswersJson = Serialize(answers), CreatedAt = now, CreatedBy = by };
            db.LogbookReports.Add(report);
        }

        report.Phase = LogbookReportPhase.Submitted;
        report.State = LogbookReportState.New;
        report.SubmittedAt = now;
        report.SubmittedBy = by;
        report.UpdatedAt = now;
        return await SaveChangesAsync(report, cancellationToken);
    }

    /// <summary>For each store the report of the week (if there is one), the template version that applies, and whether handing in would reach somebody.</summary>
    public async Task<IReadOnlyList<LogbookStoreWeek>> WeekOfStoresAsync(IReadOnlyCollection<Guid> storeIds, DateOnly day, CancellationToken cancellationToken)
    {
        var week = LogbookWeek.StartOf(day);
        var reports = await db.LogbookReports.AsNoTracking().Where(r => r.Week == week && storeIds.Contains(r.StoreId)).ToListAsync(cancellationToken);
        var structure = await structures.LoadAsync(cancellationToken);
        var current = (await templates.CurrentAsync(cancellationToken)).Version;
        return [.. storeIds.Select(id =>
        {
            var report = reports.FirstOrDefault(r => r.StoreId == id);
            return new LogbookStoreWeek(id, report, report?.TemplateVersion ?? current, LogbookReach.CanReceiveReports(structure, id));
        })];
    }

    public async Task<IReadOnlyList<LogbookReportRecord>> HistoryAsync(Guid storeId, int take, CancellationToken cancellationToken) =>
        await db.LogbookReports.AsNoTracking().Where(r => r.StoreId == storeId).OrderByDescending(r => r.Week).Take(take).ToListAsync(cancellationToken);

    private LogbookReportFailure? WeekFailure(DateOnly day) => CheckWeek(day) switch
    {
        LogbookWeekError.InFuture => LogbookReportFailure.FutureWeek,
        LogbookWeekError.TooOld => LogbookReportFailure.WeekTooOld,
        _ => null,
    };

    /// <summary>The canonical form: trimmed text, no empty answers, options in a fixed order, so the same content is always the same JSON (no change = no revision).</summary>
    private static Dictionary<Guid, LogbookAnswer> Canonical(IReadOnlyDictionary<Guid, LogbookAnswer> answers) =>
        answers
            .Select(a => (a.Key, Answer: new LogbookAnswer([.. a.Value.OptionIds.Order()], string.IsNullOrWhiteSpace(a.Value.Text) ? null : a.Value.Text.Trim())))
            .Where(a => a.Answer.OptionIds.Count > 0 || a.Answer.Text is not null)
            .ToDictionary(a => a.Key, a => a.Answer);

    private static string Serialize(Dictionary<Guid, LogbookAnswer> canonical) => JsonSerializer.Serialize(canonical.OrderBy(a => a.Key).ToDictionary(a => a.Key, a => a.Value), Json);

    private async Task<LogbookReportResult> SaveChangesAsync(LogbookReportRecord report, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (db.Entry(report).State == EntityState.Added)
        {
            db.Entry(report).State = EntityState.Detached;
            return new(LogbookReportFailure.Conflict);
        }

        return new(LogbookReportFailure.None, report);
    }
}
