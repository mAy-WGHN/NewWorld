using Portal.Core.Logbook;

namespace Portal.Core.Tests;

/// <summary>
/// The evaluation of the weekly reports (decision 49): when a report is on time (handed in until the due day and time of the settings, in local time), which
/// weeks of a year are due and still missing, and the figures per store that are added up for a region and a district: how many were handed in, how many on
/// time, the average of the rating 1 to 5 and how it is spread, how many were escalated, answered and closed.
/// </summary>
public sealed class LogbookEvaluationTests
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
    private static readonly Guid Store = Guid.Parse("00000000-0000-0000-0000-0000000000a1");

    private static LogbookReminderSettings Due(DayOfWeek day = DayOfWeek.Friday, int hour = 18, int minute = 0) => LogbookReminderSettings.Default with { DueDay = day, DueTime = new TimeOnly(hour, minute) };

    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute = 0, int second = 0) => new(year, month, day, hour, minute, second, TimeSpan.Zero);

    private static LogbookWeekFigures Week(DateOnly week, bool onTime = true, int? rating = null, bool escalated = false, bool answered = false, bool done = false) =>
        new(Store, week, onTime, rating, escalated, answered, done);

    // ---------------------------------------------------------------- when is a report on time

    [Fact]
    public void The_due_moment_is_the_due_day_and_time_of_the_week_in_local_time()
    {
        LogbookReminderPlan.DueAt(Due(), new DateOnly(2026, 10, 19), Berlin).ShouldBe(Utc(2026, 10, 23, 16)); // Friday 18:00 CEST
        LogbookReminderPlan.DueAt(Due(), new DateOnly(2026, 10, 26), Berlin).ShouldBe(Utc(2026, 10, 30, 17)); // the clock was set back on 2026-10-25: 18:00 CET
        LogbookReminderPlan.DueAt(Due(), new DateOnly(2027, 3, 22), Berlin).ShouldBe(Utc(2027, 3, 26, 17)); // two days before the clock goes forward
        LogbookReminderPlan.DueAt(Due(DayOfWeek.Monday, 9, 30), new DateOnly(2026, 10, 19), Berlin).ShouldBe(Utc(2026, 10, 19, 7, 30)); // the due day can be the Monday itself
        LogbookReminderPlan.DueAt(Due(DayOfWeek.Sunday, 23, 59), new DateOnly(2026, 10, 19), Berlin).ShouldBe(Utc(2026, 10, 25, 22, 59)); // the last minute of the week
    }

    [Fact]
    public void A_due_time_that_does_not_exist_or_exists_twice_on_the_day_of_the_change_of_the_clock_is_still_one_moment()
    {
        // Sunday 2027-03-28 02:30 does not exist (02:00 CET becomes 03:00 CEST): 03:30 CEST
        LogbookReminderPlan.DueAt(Due(DayOfWeek.Sunday, 2, 30), new DateOnly(2027, 3, 22), Berlin).ShouldBe(Utc(2027, 3, 28, 1, 30));
        // Sunday 2026-10-25 02:30 exists twice: the later one
        LogbookReminderPlan.DueAt(Due(DayOfWeek.Sunday, 2, 30), new DateOnly(2026, 10, 19), Berlin).ShouldBe(Utc(2026, 10, 25, 1, 30));
    }

    [Fact]
    public void A_report_is_on_time_until_the_second_of_the_due_moment_and_late_after_it()
    {
        var week = new DateOnly(2026, 10, 19);
        var due = Utc(2026, 10, 23, 16);

        LogbookEvaluation.IsOnTime(Due(), week, due.AddDays(-4), Berlin).ShouldBeTrue(); // early in the week
        LogbookEvaluation.IsOnTime(Due(), week, due.AddSeconds(-1), Berlin).ShouldBeTrue();
        LogbookEvaluation.IsOnTime(Due(), week, due, Berlin).ShouldBeTrue(); // exactly at the due moment
        LogbookEvaluation.IsOnTime(Due(), week, due.AddSeconds(1), Berlin).ShouldBeFalse();
        LogbookEvaluation.IsOnTime(Due(), week, due.AddDays(10), Berlin).ShouldBeFalse(); // a late report is still handed in
    }

    // ---------------------------------------------------------------- which weeks of a year are due

    [Fact]
    public void The_weeks_of_a_year_are_the_weeks_whose_monday_is_in_that_year_and_whose_due_moment_has_passed()
    {
        var justBefore = LogbookEvaluation.OverdueWeeks(2026, Utc(2026, 10, 23, 15, 59, 59), Due(), Berlin);
        var atDue = LogbookEvaluation.OverdueWeeks(2026, Utc(2026, 10, 23, 16), Due(), Berlin);

        justBefore.Count.ShouldBe(41);
        atDue.Count.ShouldBe(42);
        atDue[0].ShouldBe(new DateOnly(2026, 1, 5)); // 2026-01-01 is a Thursday: the week of Monday 2025-12-29 belongs to 2025
        atDue[^1].ShouldBe(new DateOnly(2026, 10, 19));
        atDue.Zip(atDue.Skip(1)).ShouldAllBe(pair => pair.Second == pair.First.AddDays(7));
    }

    [Fact]
    public void A_week_that_starts_in_december_belongs_to_that_year_even_if_it_is_due_in_january()
    {
        var after = Utc(2026, 1, 8, 12); // the first week of 2026 (Monday 01-05) is due on Friday 01-09

        LogbookEvaluation.OverdueWeeks(2025, after, Due(), Berlin)[^1].ShouldBe(new DateOnly(2025, 12, 29)); // due on Friday 2026-01-02
        LogbookEvaluation.OverdueWeeks(2026, after, Due(), Berlin).ShouldBeEmpty(); // nothing of 2026 is due yet
    }

    [Fact]
    public void A_year_that_is_over_has_all_its_weeks_and_a_year_that_has_not_begun_has_none()
    {
        var now = Utc(2028, 6, 1, 12);

        LogbookEvaluation.OverdueWeeks(2026, now, Due(), Berlin).Count.ShouldBe(52); // 2026: Mondays from 01-05 to 12-28
        LogbookEvaluation.OverdueWeeks(2027, now, Due(), Berlin).Count.ShouldBe(52); // 2027: 01-04 to 12-27
        LogbookEvaluation.OverdueWeeks(2029, now, Due(), Berlin).ShouldBeEmpty();
    }

    // ---------------------------------------------------------------- the figures of one store

    [Fact]
    public void The_figures_of_a_store_count_what_was_handed_in_on_time_and_late_and_what_is_missing()
    {
        var weeks = Enumerable.Range(0, 5).Select(i => new DateOnly(2026, 10, 5).AddDays(7 * i)).ToList(); // five weeks are due
        var submitted = new[]
        {
            Week(weeks[0], onTime: true, rating: 4, answered: true, done: true),
            Week(weeks[1], onTime: false, rating: 2, escalated: true),
            Week(weeks[2], onTime: true, rating: null),
        };

        var figures = LogbookEvaluation.ForStore(submitted, weeks);

        figures.Submitted.ShouldBe(3);
        figures.OnTime.ShouldBe(2);
        figures.Late.ShouldBe(1);
        figures.Missing.ShouldBe(2); // the two weeks without a report
        figures.Expected.ShouldBe(5);
        figures.OnTimeRate.ShouldBe(0.4);
        figures.RatedCount.ShouldBe(2);
        figures.RatingSum.ShouldBe(6);
        figures.RatingAverage.ShouldBe(3.0);
        figures.Distribution.ShouldBe([0, 1, 0, 1, 0]);
        figures.Escalated.ShouldBe(1);
        figures.Answered.ShouldBe(1);
        figures.Done.ShouldBe(1);
    }

    [Fact]
    public void A_report_for_a_week_that_is_not_due_yet_counts_as_handed_in_and_does_not_make_the_rate_better_than_it_is()
    {
        var due = new[] { new DateOnly(2026, 10, 12) };
        var submitted = new[] { Week(new DateOnly(2026, 10, 12), onTime: false), Week(new DateOnly(2026, 10, 19), onTime: true) }; // the second one is for the current week

        var figures = LogbookEvaluation.ForStore(submitted, due);

        figures.Submitted.ShouldBe(2);
        figures.Missing.ShouldBe(0);
        figures.Expected.ShouldBe(2); // what was handed in or is overdue
        figures.OnTime.ShouldBe(1);
        figures.OnTimeRate.ShouldBe(0.5);
    }

    [Fact]
    public void A_store_without_reports_is_missing_every_due_week_and_has_no_average()
    {
        var figures = LogbookEvaluation.ForStore([], [new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 12)]);

        figures.Submitted.ShouldBe(0);
        figures.Missing.ShouldBe(2);
        figures.OnTimeRate.ShouldBe(0.0);
        figures.RatingAverage.ShouldBeNull(); // not zero: there is nothing to average
        figures.Distribution.ShouldBe([0, 0, 0, 0, 0]);
    }

    [Fact]
    public void Nothing_due_and_nothing_handed_in_gives_no_rate_at_all()
    {
        var figures = LogbookEvaluation.ForStore([], []);

        figures.Expected.ShouldBe(0);
        figures.OnTimeRate.ShouldBeNull();
        figures.RatingAverage.ShouldBeNull();
        figures.ShouldBe(LogbookFigures.Empty, comparer: new FiguresComparer());
    }

    // ---------------------------------------------------------------- added up for a region or a district

    [Fact]
    public void Figures_are_added_up_and_the_average_is_weighted_by_the_number_of_ratings_not_the_average_of_averages()
    {
        var one = LogbookEvaluation.ForStore([Week(new DateOnly(2026, 10, 5), rating: 5)], [new DateOnly(2026, 10, 5)]); // one rating, 5
        var many = LogbookEvaluation.ForStore(
            [Week(new DateOnly(2026, 10, 5), rating: 1), Week(new DateOnly(2026, 10, 12), onTime: false, rating: 1, escalated: true), Week(new DateOnly(2026, 10, 19), rating: 1, done: true)],
            [new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 26)]); // three ratings, 1 each, one week missing

        var total = LogbookFigures.Combine([one, many]);

        total.Submitted.ShouldBe(4);
        total.OnTime.ShouldBe(3);
        total.Late.ShouldBe(1);
        total.Missing.ShouldBe(1);
        total.Expected.ShouldBe(5);
        total.OnTimeRate.ShouldBe(0.6);
        total.RatedCount.ShouldBe(4);
        total.RatingAverage.ShouldBe(2.0); // (5 + 1 + 1 + 1) / 4, not (5 + 1) / 2
        total.Distribution.ShouldBe([3, 0, 0, 0, 1]);
        total.Escalated.ShouldBe(1);
        total.Done.ShouldBe(1);
    }

    [Fact]
    public void Nothing_to_add_up_gives_the_empty_figures()
    {
        LogbookFigures.Combine([]).ShouldBe(LogbookFigures.Empty, comparer: new FiguresComparer());
    }

    /// <summary>The figures hold a list, so records are not equal by value; compare what they say.</summary>
    private sealed class FiguresComparer : IEqualityComparer<LogbookFigures>
    {
        public bool Equals(LogbookFigures? x, LogbookFigures? y) =>
            x is not null && y is not null
            && (x.Submitted, x.OnTime, x.Late, x.Missing, x.RatedCount, x.RatingSum, x.Escalated, x.Answered, x.Done) == (y.Submitted, y.OnTime, y.Late, y.Missing, y.RatedCount, y.RatingSum, y.Escalated, y.Answered, y.Done)
            && x.Distribution.SequenceEqual(y.Distribution);

        public int GetHashCode(LogbookFigures obj) => obj.Submitted;
    }
}
