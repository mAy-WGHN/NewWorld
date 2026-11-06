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

    [Fact]
    public void Augsburger_friedensfest_applies_only_to_stores_in_augsburg_in_bavaria()
    {
        var day = new DateOnly(2025, 8, 8);

        Calendar.IsHoliday(day, "BY", "Augsburg").ShouldBeTrue();
        Calendar.IsHoliday(day, "BY", " augsburg ").ShouldBeTrue();
        Calendar.IsHoliday(day, "BY", "München").ShouldBeFalse();
        Calendar.IsHoliday(day, "BY").ShouldBeFalse();
        Calendar.IsHoliday(day, "NW", "Augsburg").ShouldBeFalse();
        Calendar.IsHoliday(new DateOnly(2025, 8, 9), "BY", "Augsburg").ShouldBeFalse();
    }

    [Fact]
    public void Assumption_day_is_not_counted_in_bavaria_until_decided()
    {
        Calendar.IsHoliday(new DateOnly(2025, 8, 15), "BY", "Augsburg").ShouldBeFalse();
        Calendar.IsHoliday(new DateOnly(2025, 8, 15), "SL").ShouldBeTrue();
    }

    [Fact]
    public void Bavarian_holidays_apply_by_default_in_the_options()
    {
        new WorkforceOptions().HolidayState.ShouldBe("BY");
        Calendar.IsHoliday(new DateOnly(2026, 1, 6), "BY").ShouldBeTrue();   // Heilige Drei Könige
        Calendar.IsHoliday(new DateOnly(2026, 11, 1), "BY").ShouldBeTrue();  // Allerheiligen
        Calendar.IsHoliday(new DateOnly(2026, 6, 4), "BY").ShouldBeTrue();   // Fronleichnam
    }

    [Theory]
    [InlineData("2026-01-01", "BY", null, "Neujahr")]
    [InlineData("2026-01-06", "BY", null, "Heilige Drei Könige")]
    [InlineData("2026-04-03", "BY", null, "Karfreitag")]
    [InlineData("2026-04-05", "BY", null, null)] // Easter Sunday is no extra holiday day
    [InlineData("2026-04-06", "BY", null, "Ostermontag")]
    [InlineData("2026-05-01", "BY", null, "Tag der Arbeit")]
    [InlineData("2026-05-14", "BY", null, "Christi Himmelfahrt")]
    [InlineData("2026-05-25", "BY", null, "Pfingstmontag")]
    [InlineData("2026-06-04", "BY", null, "Fronleichnam")]
    [InlineData("2026-06-04", "BE", null, null)]
    [InlineData("2026-08-08", "BY", "Augsburg", "Augsburger Friedensfest")]
    [InlineData("2026-08-08", "BY", "München", null)]
    [InlineData("2026-10-03", "BY", null, "Tag der Deutschen Einheit")]
    [InlineData("2026-11-01", "BY", null, "Allerheiligen")]
    [InlineData("2026-11-18", "SN", null, "Buß- und Bettag")]
    [InlineData("2026-12-25", "BY", null, "1. Weihnachtstag")]
    [InlineData("2026-12-26", "BY", null, "2. Weihnachtstag")]
    [InlineData("2028-04-14", "BY", null, "Karfreitag")] // leap year
    public void Holidays_have_their_name_and_a_normal_day_has_none(string date, string state, string? city, string? name) =>
        Calendar.HolidayName(DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture), state, city).ShouldBe(name);

    [Fact]
    public void Without_a_state_only_the_nationwide_holidays_have_a_name()
    {
        Calendar.HolidayName(new DateOnly(2026, 1, 6), null).ShouldBeNull();
        Calendar.HolidayName(new DateOnly(2026, 12, 25), null).ShouldBe("1. Weihnachtstag");
    }

    [Fact]
    public void Is_holiday_and_the_name_always_agree_over_a_whole_leap_year()
    {
        for (var day = new DateOnly(2028, 1, 1); day.Year == 2028; day = day.AddDays(1))
        {
            Calendar.IsHoliday(day, "BY", "Augsburg").ShouldBe(Calendar.HolidayName(day, "BY", "Augsburg") is not null, day.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
