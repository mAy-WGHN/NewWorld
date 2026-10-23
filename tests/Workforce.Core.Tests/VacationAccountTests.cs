using Workforce.Contracts;
using Workforce.Core.Absences;

namespace Workforce.Core.Tests;

public class VacationAccountTests
{
    private static readonly WorkCalendar Calendar = new(WorkingWeek.MondayToSaturday, new GermanHolidayCalendar(), null);

    private static VacationBooking Booking(string from, string to, AbsenceStatus status) => new(
        DateRange.Of(DateOnly.Parse(from, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(to, System.Globalization.CultureInfo.InvariantCulture)), status);

    private static VacationBalance Balance(int year, decimal entitlement, string today, params VacationBooking[] bookings) =>
        VacationAccount.Compute(year, entitlement, bookings, DateOnly.Parse(today, System.Globalization.CultureInfo.InvariantCulture), Calendar);

    [Fact]
    public void Approved_days_in_the_past_are_taken_and_future_ones_are_planned()
    {
        var balance = Balance(2026, 30, "2026-07-15", Booking("2026-07-06", "2026-07-11", AbsenceStatus.Approved), Booking("2026-08-03", "2026-08-08", AbsenceStatus.Approved));

        balance.ShouldBe(new VacationBalance(2026, 30, Taken: 6, Planned: 6, Remaining: 18));
    }

    [Fact]
    public void Today_is_still_planned_and_becomes_taken_tomorrow()
    {
        var booking = Booking("2026-07-15", "2026-07-15", AbsenceStatus.Approved);

        Balance(2026, 30, "2026-07-15", booking).ShouldBe(new VacationBalance(2026, 30, 0, 1, 29));
        Balance(2026, 30, "2026-07-16", booking).ShouldBe(new VacationBalance(2026, 30, 1, 0, 29));
    }

    [Fact]
    public void Requested_days_are_reserved_as_planned()
    {
        Balance(2026, 30, "2026-07-15", Booking("2026-08-03", "2026-08-07", AbsenceStatus.Requested)).Planned.ShouldBe(5);
    }

    [Theory]
    [InlineData(AbsenceStatus.Rejected)]
    [InlineData(AbsenceStatus.Withdrawn)]
    [InlineData(AbsenceStatus.Cancelled)]
    public void Rejected_withdrawn_and_cancelled_vacation_is_credited(AbsenceStatus status) =>
        Balance(2026, 30, "2026-07-15", Booking("2026-08-03", "2026-08-07", status)).ShouldBe(new VacationBalance(2026, 30, 0, 0, 30));

    [Fact]
    public void Vacation_across_the_year_end_counts_in_each_year_separately()
    {
        var booking = Booking("2026-12-28", "2027-01-05", AbsenceStatus.Approved);

        Balance(2026, 30, "2026-12-01", booking).Planned.ShouldBe(4);
        Balance(2027, 30, "2026-12-01", booking).Planned.ShouldBe(3); // 2 Jan (Sat), 4 and 5 Jan; 1 Jan is a holiday
    }

    [Fact]
    public void Bookings_of_other_years_are_ignored() =>
        Balance(2027, 30, "2027-01-10", Booking("2026-03-02", "2026-03-06", AbsenceStatus.Approved)).ShouldBe(new VacationBalance(2027, 30, 0, 0, 30));

    [Fact]
    public void Remaining_can_become_negative_when_the_entitlement_is_lowered_afterwards() =>
        Balance(2026, 2, "2026-01-02", Booking("2026-08-03", "2026-08-07", AbsenceStatus.Approved)).Remaining.ShouldBe(-3);

    [Fact]
    public void Leap_year_vacation_counts_the_leap_day() =>
        Balance(2028, 30, "2028-01-02", Booking("2028-02-28", "2028-03-01", AbsenceStatus.Approved)).Planned.ShouldBe(3);

    [Fact]
    public void Half_day_entitlements_are_possible() =>
        Balance(2026, 27.5m, "2026-01-02", Booking("2026-08-03", "2026-08-03", AbsenceStatus.Approved)).Remaining.ShouldBe(26.5m);
}
