using Workforce.Core.Absences;

namespace Workforce.Core.Tests;

/// <summary>Sickness during approved vacation: the vacation is cancelled for the affected days and credited (plan, phase 6).</summary>
public class SicknessAdjustmentTests
{
    private static readonly WorkCalendar Calendar = new(WorkingWeek.MondayToSaturday, new GermanHolidayCalendar(), null);

    private static DateRange R(string from, string? to = null) =>
        new(DateOnly.Parse(from, System.Globalization.CultureInfo.InvariantCulture), to is null ? null : DateOnly.Parse(to, System.Globalization.CultureInfo.InvariantCulture));

    // Vacation Mon 2026-07-13 .. Sat 2026-07-25 (11 working days incl. two Saturdays... Mon-Sat x2 = 12)
    private static readonly DateRange Vacation = R("2026-07-13", "2026-07-25");

    [Fact]
    public void Sickness_in_the_middle_splits_the_vacation_in_two()
    {
        var plan = SicknessAdjustment.Plan(Vacation, R("2026-07-16", "2026-07-17"), Calendar);

        plan.Before.ShouldBe(R("2026-07-13", "2026-07-15"));
        plan.After.ShouldBe(R("2026-07-18", "2026-07-25"));
        plan.IsCompletelyCancelled.ShouldBeFalse();
    }

    [Fact]
    public void Sickness_at_the_start_leaves_only_the_end()
    {
        var plan = SicknessAdjustment.Plan(Vacation, R("2026-07-10", "2026-07-15"), Calendar);

        plan.Before.ShouldBeNull();
        plan.After.ShouldBe(R("2026-07-16", "2026-07-25"));
    }

    [Fact]
    public void Sickness_at_the_end_leaves_only_the_start()
    {
        var plan = SicknessAdjustment.Plan(Vacation, R("2026-07-22", "2026-08-02"), Calendar);

        plan.Before.ShouldBe(R("2026-07-13", "2026-07-21"));
        plan.After.ShouldBeNull();
    }

    [Fact]
    public void Open_ended_sickness_cancels_the_rest_of_the_vacation()
    {
        var plan = SicknessAdjustment.Plan(Vacation, R("2026-07-16"), Calendar);

        plan.Before.ShouldBe(R("2026-07-13", "2026-07-15"));
        plan.After.ShouldBeNull();
    }

    [Fact]
    public void Sickness_covering_the_whole_vacation_cancels_it_completely()
    {
        var plan = SicknessAdjustment.Plan(Vacation, R("2026-07-01", "2026-08-01"), Calendar);

        plan.IsCompletelyCancelled.ShouldBeTrue();
        plan.Before.ShouldBeNull();
        plan.After.ShouldBeNull();
    }

    [Fact]
    public void Remaining_pieces_without_a_single_working_day_are_dropped()
    {
        // Vacation Mon-Sun, sickness from Tuesday to Saturday: only Sunday would remain.
        var plan = SicknessAdjustment.Plan(R("2026-07-13", "2026-07-19"), R("2026-07-14", "2026-07-18"), Calendar);

        plan.Before.ShouldBe(R("2026-07-13", "2026-07-13"));
        plan.After.ShouldBeNull();
    }

    [Fact]
    public void Sickness_across_the_year_end_and_the_leap_day_cuts_by_calendar_days()
    {
        var yearEnd = SicknessAdjustment.Plan(R("2026-12-28", "2027-01-08"), R("2026-12-31", "2027-01-02"), Calendar);
        var leap = SicknessAdjustment.Plan(R("2028-02-27", "2028-03-02"), R("2028-02-29", "2028-02-29"), Calendar);

        yearEnd.Before.ShouldBe(R("2026-12-28", "2026-12-30"));
        yearEnd.After.ShouldBe(R("2027-01-03", "2027-01-08"));
        leap.Before.ShouldBe(R("2028-02-27", "2028-02-28"));
        leap.After.ShouldBe(R("2028-03-01", "2028-03-02"));
    }

    [Fact]
    public void Sickness_outside_the_vacation_changes_nothing()
    {
        var plan = SicknessAdjustment.Plan(Vacation, R("2026-07-27", "2026-07-30"), Calendar);

        plan.Before.ShouldBe(Vacation);
        plan.After.ShouldBeNull();
        plan.IsUnchanged.ShouldBeTrue();
    }

    [Fact]
    public void Open_ended_vacation_is_not_possible() =>
        Should.Throw<ArgumentException>(() => SicknessAdjustment.Plan(R("2026-07-13"), R("2026-07-16"), Calendar));
}
