namespace Portal.Core.Logbook;

/// <summary>
/// What the evaluation keeps of one handed-in report: nothing of the content, only numbers and flags (so it can be kept longer than the report itself, see
/// the retention in decision 49). <see cref="Rating"/> is the overall rating 1 to 5, <c>null</c> for a report of a version without it.
/// </summary>
public sealed record LogbookWeekFigures(Guid StoreId, DateOnly Week, bool OnTime, int? Rating, bool Escalated, bool Answered, bool Done);

/// <summary>
/// The figures of a store, a region or a district for a year. Expected is what was handed in plus what is overdue and missing; the rate of the reports on
/// time is counted against that. The average is over the reports that have a rating, and an added-up average is weighted by the number of ratings.
/// </summary>
public sealed record LogbookFigures(
    int Submitted, int OnTime, int Missing, int RatedCount, int RatingSum, IReadOnlyList<int> Distribution, int Escalated, int Answered, int Done)
{
    public static LogbookFigures Empty { get; } = new(0, 0, 0, 0, 0, [0, 0, 0, 0, 0], 0, 0, 0);

    public int Late => Submitted - OnTime;

    public int Expected => Submitted + Missing;

    /// <summary>The share of the expected reports that were handed in on time (0 to 1); <c>null</c> if nothing was expected.</summary>
    public double? OnTimeRate => Expected == 0 ? null : (double)OnTime / Expected;

    /// <summary>The average of the rating (1 to 5); <c>null</c> if no report has one.</summary>
    public double? RatingAverage => RatedCount == 0 ? null : (double)RatingSum / RatedCount;

    public static LogbookFigures Combine(IEnumerable<LogbookFigures> parts)
    {
        var total = Empty;
        foreach (var part in parts)
        {
            total = new LogbookFigures(
                total.Submitted + part.Submitted, total.OnTime + part.OnTime, total.Missing + part.Missing, total.RatedCount + part.RatedCount,
                total.RatingSum + part.RatingSum, [.. total.Distribution.Zip(part.Distribution, (a, b) => a + b)],
                total.Escalated + part.Escalated, total.Answered + part.Answered, total.Done + part.Done);
        }

        return total;
    }
}

/// <summary>The rules of the evaluation (decision 49): on time, which weeks of a year are due, and the figures of a store.</summary>
public static class LogbookEvaluation
{
    /// <summary>Whether a report was handed in until the due moment of its week (the due day and time of the settings, local time). Exactly at the due moment is still on time.</summary>
    public static bool IsOnTime(LogbookReminderSettings settings, DateOnly week, DateTimeOffset submittedAt, TimeZoneInfo zone) =>
        submittedAt <= LogbookReminderPlan.DueAt(settings, week, zone);

    /// <summary>The weeks of a year whose due moment has passed. A week belongs to the year of its Monday, so the week of 2025-12-29 is a week of 2025.</summary>
    public static IReadOnlyList<DateOnly> OverdueWeeks(int year, DateTimeOffset now, LogbookReminderSettings settings, TimeZoneInfo zone)
    {
        var first = LogbookWeek.StartOf(new DateOnly(year, 1, 1));
        if (first.Year < year)
        {
            first = first.AddDays(7);
        }

        var weeks = new List<DateOnly>();
        for (var week = first; week.Year == year && LogbookReminderPlan.DueAt(settings, week, zone) <= now; week = week.AddDays(7))
        {
            weeks.Add(week);
        }

        return weeks;
    }

    /// <summary>The figures of one store: its handed-in reports of the year and the due weeks of the year (a due week without a report is missing).</summary>
    public static LogbookFigures ForStore(IReadOnlyCollection<LogbookWeekFigures> submitted, IReadOnlyCollection<DateOnly> overdueWeeks)
    {
        var handedIn = submitted.Select(s => s.Week).ToHashSet();
        var distribution = new int[5];
        foreach (var rating in submitted.Select(s => s.Rating).OfType<int>().Where(r => r is >= 1 and <= 5))
        {
            distribution[rating - 1]++;
        }

        var ratings = submitted.Select(s => s.Rating).OfType<int>().Where(r => r is >= 1 and <= 5).ToList();
        return new LogbookFigures(
            submitted.Count, submitted.Count(s => s.OnTime), overdueWeeks.Count(w => !handedIn.Contains(w)), ratings.Count, ratings.Sum(), distribution,
            submitted.Count(s => s.Escalated), submitted.Count(s => s.Answered), submitted.Count(s => s.Done));
    }
}
