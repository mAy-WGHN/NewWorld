using Workforce.Core.Absences;

namespace Workforce.Core.Tests;

public class DateRangeTests
{
    private static DateRange R(string from, string? to = null) =>
        new(DateOnly.Parse(from, System.Globalization.CultureInfo.InvariantCulture), to is null ? null : DateOnly.Parse(to, System.Globalization.CultureInfo.InvariantCulture));

    [Theory]
    [InlineData("2026-07-01", "2026-07-10", "2026-07-10", "2026-07-20", true)]   // sharing the last day counts
    [InlineData("2026-07-01", "2026-07-10", "2026-07-11", "2026-07-20", false)]  // next day does not
    [InlineData("2026-12-30", "2027-01-02", "2027-01-01", "2027-01-05", true)]   // year change
    [InlineData("2028-02-28", "2028-02-29", "2028-02-29", "2028-03-01", true)]   // leap day
    [InlineData("2028-02-28", "2028-02-28", "2028-03-01", "2028-03-01", false)]  // leap day lies between
    public void Bounded_ranges_overlap_when_they_share_a_day(string aFrom, string aTo, string bFrom, string bTo, bool expected)
    {
        R(aFrom, aTo).Overlaps(R(bFrom, bTo)).ShouldBe(expected);
        R(bFrom, bTo).Overlaps(R(aFrom, aTo)).ShouldBe(expected);
    }

    [Fact]
    public void Open_ended_range_overlaps_everything_that_ends_on_or_after_its_start()
    {
        R("2026-07-10").Overlaps(R("2026-07-01", "2026-07-10")).ShouldBeTrue();
        R("2026-07-10").Overlaps(R("2026-07-01", "2026-07-09")).ShouldBeFalse();
        R("2026-07-10").Overlaps(R("2030-01-01", "2030-01-02")).ShouldBeTrue();
        R("2026-07-10").Overlaps(R("2026-01-01")).ShouldBeTrue(); // two open ranges always overlap
    }

    [Fact]
    public void Contains_respects_open_end()
    {
        R("2026-07-10").Contains(new DateOnly(2026, 7, 9)).ShouldBeFalse();
        R("2026-07-10").Contains(new DateOnly(2099, 1, 1)).ShouldBeTrue();
        R("2026-07-10", "2026-07-12").Contains(new DateOnly(2026, 7, 13)).ShouldBeFalse();
    }

    [Fact]
    public void Range_cannot_end_before_it_starts() =>
        Should.Throw<ArgumentException>(() => DateRange.Of(new DateOnly(2026, 7, 2), new DateOnly(2026, 7, 1)));
}
