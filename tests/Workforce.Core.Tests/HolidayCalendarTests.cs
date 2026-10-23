using Workforce.Core.Absences;

namespace Workforce.Core.Tests;

public class HolidayCalendarTests
{
    private static readonly GermanHolidayCalendar Calendar = new();

    [Theory]
    [InlineData(2024, 3, 31)]
    [InlineData(2025, 4, 20)]
    [InlineData(2026, 4, 5)]
    [InlineData(2028, 4, 16)] // leap year
    [InlineData(2038, 4, 25)] // latest possible Easter date
    [InlineData(2285, 3, 22)] // earliest possible Easter date
    public void Easter_sunday_is_computed_correctly(int year, int month, int day) =>
        GermanHolidayCalendar.EasterSunday(year).ShouldBe(new DateOnly(year, month, day));

    [Theory]
    [InlineData("2026-01-01")] // Neujahr
    [InlineData("2026-04-03")] // Karfreitag
    [InlineData("2026-04-06")] // Ostermontag
    [InlineData("2026-05-01")]
    [InlineData("2026-05-14")] // Christi Himmelfahrt
    [InlineData("2026-05-25")] // Pfingstmontag
    [InlineData("2026-10-03")]
    [InlineData("2026-12-25")]
    [InlineData("2026-12-26")]
    [InlineData("2028-04-14")] // Karfreitag in a leap year
    public void National_holidays_apply_everywhere(string date)
    {
        Calendar.IsHoliday(DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture), null).ShouldBeTrue();
        Calendar.IsHoliday(DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture), "NW").ShouldBeTrue();
    }

    [Theory]
    [InlineData("2026-06-04", "NW", true)]   // Fronleichnam
    [InlineData("2026-06-04", "BE", false)]
    [InlineData("2026-01-06", "BY", true)]   // Heilige Drei Könige
    [InlineData("2026-01-06", "NW", false)]
    [InlineData("2026-03-08", "BE", true)]   // Frauentag
    [InlineData("2026-03-08", "BY", false)]
    [InlineData("2026-09-20", "TH", true)]   // Weltkindertag
    [InlineData("2026-10-31", "SN", true)]   // Reformationstag
    [InlineData("2026-10-31", "BY", false)]
    [InlineData("2026-11-01", "BW", true)]   // Allerheiligen
    [InlineData("2026-11-01", "BE", false)]
    [InlineData("2026-11-18", "SN", true)]   // Buß- und Bettag: Wednesday before 23 Nov
    [InlineData("2025-11-19", "SN", true)]
    [InlineData("2026-11-18", "NW", false)]
    [InlineData("2026-08-15", "SL", true)]   // Mariä Himmelfahrt
    [InlineData("2026-08-15", "NW", false)]
    public void State_holidays_apply_only_in_their_states(string date, string state, bool expected) =>
        Calendar.IsHoliday(DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture), state).ShouldBe(expected);

    [Fact]
    public void Without_a_state_only_national_holidays_count() =>
        Calendar.IsHoliday(new DateOnly(2026, 6, 4), null).ShouldBeFalse();

    [Fact]
    public void An_ordinary_day_is_no_holiday() => Calendar.IsHoliday(new DateOnly(2026, 7, 15), "NW").ShouldBeFalse();

    [Fact]
    public void State_codes_are_not_case_sensitive() => Calendar.IsHoliday(new DateOnly(2026, 6, 4), "nw").ShouldBeTrue();

    [Fact]
    public void Unknown_state_code_is_rejected_instead_of_silently_ignored() =>
        Should.Throw<ArgumentException>(() => Calendar.IsHoliday(new DateOnly(2026, 6, 4), "XX"));
}
