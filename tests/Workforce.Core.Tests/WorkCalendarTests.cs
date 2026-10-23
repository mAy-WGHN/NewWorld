using Workforce.Core.Absences;

namespace Workforce.Core.Tests;

public class WorkCalendarTests
{
    private static WorkCalendar Berlin(string? state = null) => new(WorkingWeek.MondayToSaturday, new GermanHolidayCalendar(), state);

    [Fact]
    public void Default_working_week_is_monday_to_saturday()
    {
        var week = WorkingWeek.MondayToSaturday;

        week.IsWorkingDay(new DateOnly(2026, 7, 11)).ShouldBeTrue();  // Saturday
        week.IsWorkingDay(new DateOnly(2026, 7, 12)).ShouldBeFalse(); // Sunday
    }

    [Fact]
    public void Working_week_is_configurable() =>
        new WorkingWeek(new HashSet<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Tuesday }).IsWorkingDay(new DateOnly(2026, 7, 11)).ShouldBeFalse();

    [Theory]
    [InlineData("2026-07-13", "2026-07-17", 5)]   // Mon-Fri
    [InlineData("2026-07-13", "2026-07-19", 6)]   // Mon-Sun: Sunday does not count
    [InlineData("2026-07-12", "2026-07-12", 0)]   // a single Sunday
    [InlineData("2026-07-15", "2026-07-15", 1)]   // single day
    [InlineData("2026-12-28", "2027-01-01", 4)]   // year end, 1 Jan is a holiday
    [InlineData("2026-12-28", "2027-01-05", 7)]   // 28-31 Dec (4), 1 Jan holiday, 2 Jan Sat (1), 3 Jan Sun, 4+5 Jan (2)
    [InlineData("2028-02-28", "2028-03-01", 3)]   // leap day 29 Feb is a normal Tuesday
    [InlineData("2026-03-27", "2026-03-30", 3)]   // across the change to summer time (Fri, Sat, Mon)
    [InlineData("2026-10-23", "2026-10-26", 3)]   // across the change to winter time
    [InlineData("2026-04-02", "2026-04-07", 3)]   // Easter: Thu, (Good Friday), Sat, (Easter Monday), Tue
    [InlineData("2026-01-30", "2026-02-02", 3)]   // month change
    public void Counts_working_days_without_sundays_and_holidays(string from, string to, int expected) =>
        Berlin().CountWorkingDays(DateOnly.Parse(from, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(to, System.Globalization.CultureInfo.InvariantCulture))
            .ShouldBe(expected);

    [Fact]
    public void State_holidays_reduce_the_count_only_in_that_state()
    {
        var from = new DateOnly(2026, 6, 1);
        var to = new DateOnly(2026, 6, 6);

        Berlin().CountWorkingDays(from, to).ShouldBe(6);
        Berlin("NW").CountWorkingDays(from, to).ShouldBe(5); // Fronleichnam 4 June
    }

    [Fact]
    public void Counts_per_year_split_at_the_year_change()
    {
        var byYear = Berlin().CountWorkingDaysByYear(new DateOnly(2026, 12, 29), new DateOnly(2027, 1, 4));

        // 29,30,31 Dec | 1 Jan holiday, 2 Jan Saturday, 3 Jan Sunday, 4 Jan Monday
        byYear.ShouldBe(new Dictionary<int, int> { [2026] = 3, [2027] = 2 });
    }

    [Fact]
    public void Reversed_range_is_rejected() =>
        Should.Throw<ArgumentException>(() => Berlin().CountWorkingDays(new DateOnly(2026, 7, 2), new DateOnly(2026, 7, 1)));
}
