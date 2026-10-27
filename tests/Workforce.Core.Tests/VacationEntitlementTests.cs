using System.Globalization;
using Workforce.Contracts;
using Workforce.Core.Absences;

namespace Workforce.Core.Tests;

/// <summary>
/// The yearly vacation entitlement: contractual days (pro rata in the year of joining, rounded up to half days), additional days that
/// expire after the first quarter of the following year and are used up first.
/// </summary>
public class VacationEntitlementTests
{
    private static readonly WorkCalendar Calendar = new(WorkingWeek.MondayToSaturday, new GermanHolidayCalendar(), null);

    private static DateOnly D(string date) => DateOnly.Parse(date, CultureInfo.InvariantCulture);

    private static VacationBooking Booking(string from, string to, AbsenceStatus status = AbsenceStatus.Approved) => new(DateRange.Of(D(from), D(to)), status);

    private static ExtraVacationDays Extra(ExtraVacationKind kind, int year, decimal days) => new(Guid.NewGuid(), kind, year, days);

    private static VacationBreakdown Compute(
        int year, decimal annual, DateOnly? entry, string today, ExtraVacationDays[] extras, params VacationBooking[] bookings) =>
        VacationAccount.Compute(year, annual, entry, extras, bookings, D(today), D(today), Calendar);

    // ---------------------------------------------------------------- pro rata in the year of joining

    [Theory]
    [InlineData(2.0833, 2.5)]   // 25 / 12 * 1 month (the example from the HR notes)
    [InlineData(23.3333, 23.5)] // 28 days of a 6-day week as a 5-day week
    [InlineData(2.5, 2.5)]
    [InlineData(2.51, 3.0)]
    [InlineData(0, 0)]
    public void Fractions_are_rounded_up_to_half_days(double value, double expected) =>
        VacationEntitlement.RoundUpToHalf((decimal)value).ShouldBe((decimal)expected);

    [Theory]
    [InlineData("2026-12-01", 1)]  // starts on the 1st: December is a full month
    [InlineData("2026-12-02", 0)]  // December is not complete
    [InlineData("2026-12-15", 0)]
    [InlineData("2026-11-30", 1)]  // only December is complete
    [InlineData("2026-03-01", 10)]
    [InlineData("2026-03-15", 9)]
    [InlineData("2026-01-01", 12)]
    [InlineData("2026-01-02", 11)]
    [InlineData("2028-02-29", 10)] // leap day: March to December
    public void Only_full_months_of_employment_in_the_year_of_joining_count(string entry, int expectedMonths) =>
        VacationEntitlement.FullMonthsInEntryYear(D(entry)).ShouldBe(expectedMonths);

    [Theory]
    [InlineData("2026-12-01", 2.5)]   // 25 / 12 * 1 = 2.083 -> 2.5
    [InlineData("2026-12-15", 0)]
    [InlineData("2026-03-01", 21)]    // 25 / 12 * 10 = 20.83 -> 21
    [InlineData("2026-03-15", 19)]    // 25 / 12 * 9 = 18.75 -> 19
    [InlineData("2026-06-01", 15)]    // 25 / 12 * 7 = 14.58 -> 15
    [InlineData("2026-01-01", 25)]    // a full year is never more than the contract
    public void The_contractual_days_in_the_year_of_joining_are_pro_rata(string entry, double expected) =>
        VacationEntitlement.Contractual(25, D(entry), 2026).ShouldBe((decimal)expected);

    [Fact]
    public void Before_the_year_of_joining_there_is_no_entitlement_and_afterwards_the_full_one()
    {
        VacationEntitlement.Contractual(25, D("2027-05-01"), 2026).ShouldBe(0m);
        VacationEntitlement.Contractual(25, D("2025-12-15"), 2026).ShouldBe(25m);
        VacationEntitlement.Contractual(25, null, 2026).ShouldBe(25m); // entry date unknown: the full entitlement
    }

    [Fact]
    public void A_half_day_contract_value_is_pro_rated_and_rounded_up()
    {
        // 23.5 / 12 * 4 months = 7.83 -> 8
        VacationEntitlement.Contractual(23.5m, D("2026-09-01"), 2026).ShouldBe(8m);
    }

    // ---------------------------------------------------------------- additional days: window and expiry

    [Fact]
    public void A_carry_over_is_usable_from_the_new_year_until_the_end_of_march()
    {
        var carryOver = Extra(ExtraVacationKind.CarryOver, 2025, 2.5m);

        carryOver.UsableFrom.ShouldBe(D("2026-01-01"));
        carryOver.ExpiresOn.ShouldBe(D("2026-03-31"));
    }

    [Fact]
    public void Additional_days_are_usable_in_their_year_and_until_the_end_of_march_of_the_next()
    {
        var additional = Extra(ExtraVacationKind.Additional, 2026, 3);

        additional.UsableFrom.ShouldBe(D("2026-01-01"));
        additional.ExpiresOn.ShouldBe(D("2027-03-31"));
    }

    [Fact]
    public void Additional_days_count_in_the_effective_days_until_they_expire()
    {
        var carryOver = Extra(ExtraVacationKind.CarryOver, 2025, 2.5m);

        var beforeExpiry = Compute(2026, 25, null, "2026-03-31", [carryOver]);
        var afterExpiry = Compute(2026, 25, null, "2026-04-01", [carryOver]);

        (beforeExpiry.ContractualDays, beforeExpiry.ExtraDays, beforeExpiry.ForfeitedExtraDays, beforeExpiry.EffectiveDays, beforeExpiry.Remaining).ShouldBe((25m, 2.5m, 0m, 27.5m, 27.5m));
        (afterExpiry.ExtraDays, afterExpiry.ForfeitedExtraDays, afterExpiry.EffectiveDays, afterExpiry.Remaining).ShouldBe((0m, 2.5m, 25m, 25m));
    }

    [Fact]
    public void Additional_days_are_used_before_the_contractual_days()
    {
        var carryOver = Extra(ExtraVacationKind.CarryOver, 2025, 2.5m);

        // Mon-Wed 2-4 Feb = 3 days: 2.5 from the carry-over, 0.5 from the contract.
        var account = Compute(2026, 25, null, "2026-03-01", [carryOver], Booking("2026-02-02", "2026-02-04"));

        account.Taken.ShouldBe(3m);
        account.Lines.Single().UsedInYear.ShouldBe(2.5m);
        account.Lines.Single().Forfeited.ShouldBe(0m);
        (account.EffectiveDays, account.Remaining).ShouldBe((27.5m, 24.5m));
    }

    [Fact]
    public void The_unused_part_of_additional_days_is_forfeited_after_the_end_of_march()
    {
        var carryOver = Extra(ExtraVacationKind.CarryOver, 2025, 2.5m);

        // 1 day used in February; 1.5 expire on 1 April.
        var account = Compute(2026, 25, null, "2026-04-01", [carryOver], Booking("2026-02-02", "2026-02-02"));

        (account.ExtraDays, account.ForfeitedExtraDays).ShouldBe((1m, 1.5m));
        (account.EffectiveDays, account.Taken, account.Remaining).ShouldBe((26m, 1m, 25m));
    }

    [Fact]
    public void Vacation_after_the_expiry_is_taken_from_the_contractual_days()
    {
        var carryOver = Extra(ExtraVacationKind.CarryOver, 2025, 2.5m);

        // Tue-Wed 7-8 April: the carry-over has expired, both days come from the contract.
        var account = Compute(2026, 25, null, "2026-04-01", [carryOver], Booking("2026-04-07", "2026-04-08", AbsenceStatus.Requested));

        (account.Planned, account.Remaining).ShouldBe((2m, 23m));
    }

    [Fact]
    public void The_earlier_expiring_days_are_used_first()
    {
        var carryOver = Extra(ExtraVacationKind.CarryOver, 2025, 2m);   // expires 2026-03-31
        var additional = Extra(ExtraVacationKind.Additional, 2026, 3);  // expires 2027-03-31

        var account = Compute(2026, 25, null, "2026-03-01", [additional, carryOver], Booking("2026-02-02", "2026-02-04"));

        account.Lines.Single(l => l.Kind == ExtraVacationKind.CarryOver).UsedInYear.ShouldBe(2m);
        account.Lines.Single(l => l.Kind == ExtraVacationKind.Additional).UsedInYear.ShouldBe(1m);
        account.Remaining.ShouldBe(25m + 5m - 3m);
    }

    [Fact]
    public void A_carry_over_for_a_later_year_does_not_exist_yet()
    {
        // Leftover of 2026 is usable from 2027 on; it is not part of the account of 2026.
        var account = Compute(2026, 25, null, "2026-11-01", [Extra(ExtraVacationKind.CarryOver, 2026, 4)]);

        account.ExtraDays.ShouldBe(0m);
        account.Lines.ShouldBeEmpty();
    }

    [Fact]
    public void Unused_additional_days_of_a_year_are_still_there_in_the_first_quarter_of_the_next()
    {
        var additional = Extra(ExtraVacationKind.Additional, 2026, 3);

        // 1 day taken in 2026 (Tue 7 July); in January 2027 two days are left, valid until the end of March 2027.
        var account = Compute(2027, 25, null, "2027-01-10", [additional], Booking("2026-07-07", "2026-07-07"));

        (account.ExtraDays, account.ForfeitedExtraDays, account.EffectiveDays).ShouldBe((2m, 0m, 27m));
        account.Lines.Single().ExpiresOn.ShouldBe(D("2027-03-31"));
    }

    [Fact]
    public void A_booking_before_the_additional_days_exist_does_not_use_them()
    {
        // The carry-over of 2025 is only usable from 2026-01-01: vacation in 2025 was paid from the contract of 2025.
        var account = Compute(2026, 25, null, "2026-02-01", [Extra(ExtraVacationKind.CarryOver, 2025, 2.5m)], Booking("2025-12-22", "2025-12-23"));

        account.Lines.Single().UsedInYear.ShouldBe(0m);
        account.EffectiveDays.ShouldBe(27.5m);
    }

    [Fact]
    public void Requested_days_use_the_additional_days_too()
    {
        var account = Compute(2026, 25, null, "2026-02-01", [Extra(ExtraVacationKind.CarryOver, 2025, 2.5m)], Booking("2026-02-09", "2026-02-10", AbsenceStatus.Requested));

        (account.Planned, account.Lines.Single().UsedInYear, account.Remaining).ShouldBe((2m, 2m, 25.5m));
    }

    [Fact]
    public void The_validity_is_checked_at_the_given_date_so_a_later_request_cannot_count_on_expiring_days()
    {
        var carryOver = Extra(ExtraVacationKind.CarryOver, 2025, 2.5m);
        var request = Booking("2026-04-13", "2026-04-17", AbsenceStatus.Requested); // 5 days after the expiry

        var today = VacationAccount.Compute(2026, 25, null, [carryOver], [request], D("2026-02-01"), D("2026-02-01"), Calendar);
        var atRequest = VacationAccount.Compute(2026, 25, null, [carryOver], [request], D("2026-02-01"), D("2026-04-17"), Calendar);

        today.Remaining.ShouldBe(27.5m - 5m);   // the carry-over could still be used by other days until the end of March
        atRequest.Remaining.ShouldBe(25m - 5m); // but for the request itself it is gone
    }

    // ---------------------------------------------------------------- joining during the year

    [Fact]
    public void The_effective_days_of_the_year_of_joining_are_the_pro_rata_contractual_days_plus_the_additional_days()
    {
        var account = Compute(2026, 25, D("2026-12-01"), "2026-12-02", [Extra(ExtraVacationKind.Additional, 2026, 1)]);

        (account.ContractualAnnualDays, account.ContractualDays, account.ExtraDays, account.EffectiveDays).ShouldBe((25m, 2.5m, 1m, 3.5m));
    }

    [Fact]
    public void Without_additional_days_and_without_joining_in_the_year_the_result_equals_the_old_calculation()
    {
        var account = Compute(2026, 30, null, "2026-07-15", [], Booking("2026-07-06", "2026-07-11"), Booking("2026-08-03", "2026-08-08"));

        (account.EffectiveDays, account.Taken, account.Planned, account.Remaining).ShouldBe((30m, 6m, 6m, 18m));
    }
}
