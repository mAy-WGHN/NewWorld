using Portal.Core.Logbook;

namespace Portal.Core.Tests;

/// <summary>
/// The week of a weekly report: it starts on Monday and counts in local time (Europe/Berlin), also around the change of the year, the 53 week year, the leap
/// day and the days of the clock change. A report can be written for the current week and a few weeks back, never for the future.
/// </summary>
public sealed class LogbookWeekTests
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    // ---------------------------------------------------------------- the Monday of a day

    [Fact]
    public void Every_day_of_a_week_belongs_to_the_Monday_before_or_on_it()
    {
        // Monday 2026-10-19 to Sunday 2026-10-25
        for (var day = 19; day <= 25; day++)
        {
            LogbookWeek.StartOf(D(2026, 10, day)).ShouldBe(D(2026, 10, 19));
        }

        LogbookWeek.StartOf(D(2026, 10, 26)).ShouldBe(D(2026, 10, 26)); // the next Monday starts a new week
    }

    [Fact]
    public void A_week_can_reach_over_the_change_of_the_month_and_the_year()
    {
        LogbookWeek.StartOf(D(2026, 12, 31)).ShouldBe(D(2026, 12, 28)); // Thursday
        LogbookWeek.StartOf(D(2027, 1, 1)).ShouldBe(D(2026, 12, 28)); // Friday of the same week
        LogbookWeek.StartOf(D(2027, 1, 3)).ShouldBe(D(2026, 12, 28)); // Sunday
        LogbookWeek.StartOf(D(2027, 1, 4)).ShouldBe(D(2027, 1, 4));
        LogbookWeek.StartOf(D(2026, 3, 1)).ShouldBe(D(2026, 2, 23)); // Sunday after a February
    }

    [Fact]
    public void The_leap_day_and_the_year_with_53_weeks_are_no_special_case()
    {
        LogbookWeek.StartOf(D(2028, 2, 29)).ShouldBe(D(2028, 2, 28)); // Tuesday
        LogbookWeek.StartOf(D(2028, 3, 1)).ShouldBe(D(2028, 2, 28));
        LogbookWeek.StartOf(D(2026, 12, 31)).ShouldBe(D(2026, 12, 28)); // 2026 has 53 ISO weeks
        LogbookWeek.StartOf(D(2020, 12, 31)).ShouldBe(D(2020, 12, 28));
    }

    // ---------------------------------------------------------------- today, in local time

    [Fact]
    public void The_week_changes_at_local_midnight_and_not_at_midnight_UTC()
    {
        // Sunday 2026-10-25 00:30 local time (CEST, UTC+2) is still Saturday 22:30 UTC: the week is the one of Monday the 19th in both readings.
        LogbookWeek.Current(new DateTimeOffset(2026, 10, 24, 22, 30, 0, TimeSpan.Zero), Berlin).ShouldBe(D(2026, 10, 19));

        // Monday 2026-11-02 00:30 local time (CET, UTC+1) is Sunday 23:30 UTC: already the new week, although UTC still says Sunday.
        LogbookWeek.Current(new DateTimeOffset(2026, 11, 1, 23, 30, 0, TimeSpan.Zero), Berlin).ShouldBe(D(2026, 11, 2));
        LogbookWeek.Current(new DateTimeOffset(2026, 11, 1, 22, 30, 0, TimeSpan.Zero), Berlin).ShouldBe(D(2026, 10, 26)); // 23:30 local time, still Sunday
    }

    [Fact]
    public void On_the_days_of_the_clock_change_the_local_day_is_still_the_right_one()
    {
        // 2026-10-25 (Sunday): 03:00 CEST becomes 02:00 CET. 01:30 UTC is 02:30 CET, 23:30 UTC is already Monday 00:30 CET.
        LogbookWeek.Current(new DateTimeOffset(2026, 10, 25, 1, 30, 0, TimeSpan.Zero), Berlin).ShouldBe(D(2026, 10, 19));
        LogbookWeek.Current(new DateTimeOffset(2026, 10, 25, 23, 30, 0, TimeSpan.Zero), Berlin).ShouldBe(D(2026, 10, 26));

        // 2027-03-28 (Sunday): 02:00 CET becomes 03:00 CEST.
        LogbookWeek.Current(new DateTimeOffset(2027, 3, 28, 0, 59, 0, TimeSpan.Zero), Berlin).ShouldBe(D(2027, 3, 22));
        LogbookWeek.Current(new DateTimeOffset(2027, 3, 28, 22, 30, 0, TimeSpan.Zero), Berlin).ShouldBe(D(2027, 3, 29));
    }

    [Fact]
    public void New_Years_eve_in_local_time_is_still_the_old_year_while_UTC_is_already_the_new_one()
    {
        // 2026-12-31 23:30 UTC is 2027-01-01 00:30 local time: a Friday, still in the week of Monday 2026-12-28.
        LogbookWeek.Current(new DateTimeOffset(2026, 12, 31, 23, 30, 0, TimeSpan.Zero), Berlin).ShouldBe(D(2026, 12, 28));
    }

    // ---------------------------------------------------------------- which weeks can be written

    [Fact]
    public void The_current_week_and_the_weeks_back_up_to_the_limit_can_be_written_nothing_else()
    {
        var current = D(2026, 10, 19);

        LogbookWeek.Check(current, current).ShouldBe(LogbookWeekError.None);
        LogbookWeek.Check(current.AddDays(-7), current).ShouldBe(LogbookWeekError.None);
        LogbookWeek.Check(current.AddDays(-7 * LogbookWeek.MaxWeeksBack), current).ShouldBe(LogbookWeekError.None);
        LogbookWeek.Check(current.AddDays(-7 * (LogbookWeek.MaxWeeksBack + 1)), current).ShouldBe(LogbookWeekError.TooOld);
        LogbookWeek.Check(current.AddDays(7), current).ShouldBe(LogbookWeekError.InFuture);
    }

    [Fact]
    public void A_day_in_the_middle_of_the_week_is_checked_as_its_week()
    {
        var current = D(2026, 10, 19);

        LogbookWeek.Check(D(2026, 10, 25), current).ShouldBe(LogbookWeekError.None); // Sunday of the current week
        LogbookWeek.Check(D(2026, 10, 26), current).ShouldBe(LogbookWeekError.InFuture); // Monday of the next one
    }

    [Fact]
    public void The_limit_counts_over_the_change_of_the_year()
    {
        var current = D(2027, 1, 4);

        LogbookWeek.Check(D(2026, 12, 28), current).ShouldBe(LogbookWeekError.None); // one week back
        LogbookWeek.Check(D(2026, 11, 9), current).ShouldBe(LogbookWeekError.None); // exactly eight weeks back
        LogbookWeek.Check(D(2026, 11, 2), current).ShouldBe(LogbookWeekError.TooOld);
    }

    [Fact]
    public void The_options_name_a_zone_that_exists_and_default_to_Berlin()
    {
        new LogbookOptions().Zone.Id.ShouldBe("Europe/Berlin");
        new LogbookOptions { TimeZoneId = "UTC" }.Zone.BaseUtcOffset.ShouldBe(TimeSpan.Zero);
    }
}
