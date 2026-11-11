using Portal.Core.Logbook;

namespace Portal.Core.Tests;

/// <summary>
/// The automatic reminder to the store managers (decision 49): when it is due (a due day and time, reminder points counted from the due day), what is
/// maintainable and checked, and that nothing is sent twice, in a burst, after the maximum, or for times before it was switched on. All in local time.
/// </summary>
public sealed class LogbookReminderTests
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    private static readonly DateOnly Week = new(2026, 10, 19); // Monday; Friday is the 23rd, Sunday the 25th (the clock changes that night), Monday the 26th

    private static LogbookReminderSettings Settings(bool enabled = true, DateTimeOffset? enabledAt = null, int max = 2, params LogbookReminderPoint[] points) =>
        new(enabled, enabledAt, DayOfWeek.Friday, new TimeOnly(18, 0), points.Length == 0 ? [new(0, new TimeOnly(12, 0)), new(3, new TimeOnly(9, 0))] : points, max);

    private static DateTimeOffset Utc(int month, int day, int hour, int minute = 0) => new(2026, month, day, hour, minute, 0, TimeSpan.Zero);

    // ---------------------------------------------------------------- when a point is due

    [Fact]
    public void A_point_is_counted_from_the_due_day_in_local_time_also_over_the_change_of_the_clock()
    {
        var settings = Settings();

        // Friday 2026-10-23 12:00 CEST = 10:00 UTC; Monday 2026-10-26 09:00 CET (the clock was set back on Sunday) = 08:00 UTC.
        LogbookReminderPlan.ScheduledAt(settings, Week, 0, Berlin).ShouldBe(Utc(10, 23, 10));
        LogbookReminderPlan.ScheduledAt(settings, Week, 1, Berlin).ShouldBe(Utc(10, 26, 8));
    }

    [Fact]
    public void A_time_that_does_not_exist_on_the_day_the_clock_goes_forward_is_moved_to_the_hour_after()
    {
        // Week of 2027-03-22: Friday 03-26; two days later is Sunday 03-28, 02:30 does not exist (02:00 CET becomes 03:00 CEST).
        var settings = Settings(points: new LogbookReminderPoint(2, new TimeOnly(2, 30)));

        LogbookReminderPlan.ScheduledAt(settings, new DateOnly(2027, 3, 22), 0, Berlin).ShouldBe(new DateTimeOffset(2027, 3, 28, 1, 30, 0, TimeSpan.Zero)); // 03:30 CEST
    }

    [Fact]
    public void A_time_that_exists_twice_when_the_clock_goes_back_is_taken_once_at_the_later_instant()
    {
        // 2026-10-25 02:30 happens twice (CEST and then CET); the later one is 01:30 UTC.
        var settings = Settings(points: new LogbookReminderPoint(2, new TimeOnly(2, 30)));

        LogbookReminderPlan.ScheduledAt(settings, Week, 0, Berlin).ShouldBe(Utc(10, 25, 1, 30));
    }

    [Fact]
    public void The_points_are_taken_in_time_order_no_matter_how_they_were_entered()
    {
        var settings = Settings(points: [new(3, new TimeOnly(9, 0)), new(0, new TimeOnly(12, 0)), new(0, new TimeOnly(8, 0))]);

        LogbookReminderPlan.Ordered(settings).Select(p => (p.DaysAfterDue, p.Time)).ShouldBe([(0, new TimeOnly(8, 0)), (0, new TimeOnly(12, 0)), (3, new TimeOnly(9, 0))]);
    }

    // ---------------------------------------------------------------- what is due now

    [Fact]
    public void Nothing_is_due_before_the_first_point_and_the_first_point_is_due_at_its_time()
    {
        var settings = Settings();

        LogbookReminderPlan.DuePoint(settings, Week, Utc(10, 23, 9, 59), Berlin, lastSentPoint: null, sentCount: 0).ShouldBeNull();
        LogbookReminderPlan.DuePoint(settings, Week, Utc(10, 23, 10, 0), Berlin, null, 0).ShouldBe(0);
    }

    [Fact]
    public void The_second_point_follows_after_the_first_was_sent_and_a_point_is_never_sent_twice()
    {
        var settings = Settings();

        LogbookReminderPlan.DuePoint(settings, Week, Utc(10, 23, 15), Berlin, lastSentPoint: 0, sentCount: 1).ShouldBeNull(); // the first one was sent, the second is not yet due
        LogbookReminderPlan.DuePoint(settings, Week, Utc(10, 26, 8, 0), Berlin, lastSentPoint: 0, sentCount: 1).ShouldBe(1);
        LogbookReminderPlan.DuePoint(settings, Week, Utc(10, 27, 8, 0), Berlin, lastSentPoint: 1, sentCount: 2).ShouldBeNull();
    }

    [Fact]
    public void If_several_points_are_overdue_only_the_last_one_is_sent_not_a_burst()
    {
        var settings = Settings(max: 5);

        // the service was down until Tuesday: both points are overdue, one reminder is enough
        LogbookReminderPlan.DuePoint(settings, Week, Utc(10, 27, 8, 0), Berlin, lastSentPoint: null, sentCount: 0).ShouldBe(1);
    }

    [Fact]
    public void The_maximum_per_store_and_week_stops_the_reminders_manual_ones_count_with()
    {
        var settings = Settings(max: 1);

        LogbookReminderPlan.DuePoint(settings, Week, Utc(10, 26, 8, 0), Berlin, lastSentPoint: null, sentCount: 1).ShouldBeNull(); // a manual reminder already counted
        LogbookReminderPlan.DuePoint(settings, Week, Utc(10, 26, 8, 0), Berlin, null, 0).ShouldBe(1);
    }

    [Fact]
    public void A_switched_off_reminder_sends_nothing_and_points_before_it_was_switched_on_are_skipped()
    {
        LogbookReminderPlan.DuePoint(Settings(enabled: false), Week, Utc(10, 27, 8), Berlin, null, 0).ShouldBeNull();

        var onSinceFriday = Settings(enabledAt: Utc(10, 23, 11)); // after the first point (12:00 local = 10:00 UTC), before the second
        LogbookReminderPlan.DuePoint(onSinceFriday, Week, Utc(10, 23, 15), Berlin, null, 0).ShouldBeNull(); // the first point was before
        LogbookReminderPlan.DuePoint(onSinceFriday, Week, Utc(10, 26, 8, 0), Berlin, null, 0).ShouldBe(1);
    }

    [Fact]
    public void The_week_that_is_not_due_yet_has_nothing_due()
    {
        LogbookReminderPlan.DuePoint(Settings(), Week.AddDays(7), Utc(10, 27, 8), Berlin, null, 0).ShouldBeNull();
    }

    [Fact]
    public void The_weeks_that_can_still_have_a_due_point_are_the_current_one_and_the_ones_the_points_reach_back_into()
    {
        var current = new DateOnly(2026, 10, 26);

        // the last point is 3 days after the due day (Friday): its Monday belongs to the week after, so the week before is still of interest
        LogbookReminderPlan.WeeksToCheck(Settings(), current).ShouldBe([current, current.AddDays(-7)]);
        // 14 days after Friday reaches into the week after the next one: two weeks back
        LogbookReminderPlan.WeeksToCheck(Settings(points: new LogbookReminderPoint(14, new TimeOnly(9, 0))), current).ShouldBe([current, current.AddDays(-7), current.AddDays(-14)]);
    }

    // ---------------------------------------------------------------- what may be maintained

    [Fact]
    public void The_default_is_valid_and_switched_on_with_the_start_values()
    {
        var settings = LogbookReminderSettings.Default;

        LogbookReminderValidator.Validate(settings).ShouldBeEmpty();
        settings.Enabled.ShouldBeTrue();
        settings.DueDay.ShouldBe(DayOfWeek.Friday);
        settings.DueTime.ShouldBe(new TimeOnly(18, 0));
        settings.Points.Select(p => (p.DaysAfterDue, p.Time)).ShouldBe([(0, new TimeOnly(12, 0)), (3, new TimeOnly(9, 0))]);
        settings.MaxReminders.ShouldBe(2);
    }

    [Fact]
    public void The_settings_are_checked_points_maximum_and_due_day()
    {
        var tooManyPoints = Settings(points: [.. Enumerable.Range(0, LogbookReminderValidator.MaxPoints + 1).Select(i => new LogbookReminderPoint(i % 14, new TimeOnly(8 + i % 10, 0)))]);
        var errors = (LogbookReminderSettings s) => LogbookReminderValidator.Validate(s);

        errors(tooManyPoints).ShouldContain(LogbookReminderError.TooManyPoints);
        errors(Settings(points: new LogbookReminderPoint(-1, new TimeOnly(9, 0)))).ShouldContain(LogbookReminderError.DaysOutOfRange);
        errors(Settings(points: new LogbookReminderPoint(15, new TimeOnly(9, 0)))).ShouldContain(LogbookReminderError.DaysOutOfRange);
        errors(Settings(points: [new(1, new TimeOnly(9, 0)), new(1, new TimeOnly(9, 0))])).ShouldContain(LogbookReminderError.DuplicatePoint);
        errors(Settings(max: 0)).ShouldContain(LogbookReminderError.MaxOutOfRange);
        errors(Settings(max: LogbookReminderValidator.MaxRemindersLimit + 1)).ShouldContain(LogbookReminderError.MaxOutOfRange);
        errors(Settings() with { DueDay = (DayOfWeek)9 }).ShouldContain(LogbookReminderError.DueDayInvalid);
        errors(Settings(points: [])).ShouldBeEmpty(); // the default points apply when none are given
    }

    [Fact]
    public void Without_any_point_there_is_no_reminder_and_that_is_allowed()
    {
        var none = new LogbookReminderSettings(true, null, DayOfWeek.Friday, new TimeOnly(18, 0), [], 2);

        LogbookReminderValidator.Validate(none).ShouldBeEmpty();
        LogbookReminderPlan.DuePoint(none, Week, Utc(10, 30, 8), Berlin, null, 0).ShouldBeNull();
    }

    [Fact]
    public void Switching_on_sets_the_time_and_switching_off_or_changing_other_things_keeps_it()
    {
        var now = Utc(11, 1, 10);
        var off = LogbookReminderSettings.Default with { Enabled = false, EnabledAt = Utc(10, 1, 10) };

        LogbookReminderPlan.Apply(off, off with { Enabled = true }, now).EnabledAt.ShouldBe(now); // switched on now: earlier points do not count
        LogbookReminderPlan.Apply(LogbookReminderSettings.Default with { EnabledAt = Utc(10, 1, 10) }, LogbookReminderSettings.Default with { MaxReminders = 3 }, now).EnabledAt.ShouldBe(Utc(10, 1, 10)); // still on: kept
        LogbookReminderPlan.Apply(LogbookReminderSettings.Default with { EnabledAt = Utc(10, 1, 10) }, LogbookReminderSettings.Default with { Enabled = false }, now).Enabled.ShouldBeFalse();
        LogbookReminderPlan.Apply(null, LogbookReminderSettings.Default, now).EnabledAt.ShouldBe(now); // the first time
    }
}
