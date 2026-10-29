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

    // The tests of the expiry use the end of the first quarter as the threshold; without a threshold nothing expires (see the last tests).
    private static readonly VacationRules Rules = VacationRules.EndOfFirstQuarter;

    private static VacationBreakdown Compute(
        int year, decimal annual, DateOnly? entry, string today, ExtraVacationDays[] extras, params VacationBooking[] bookings) =>
        VacationAccount.Compute(year, annual, entry, extras, Rules, bookings, D(today), D(today), Calendar);

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

    // ---------------------------------------------------------------- leaving during the year: the same formula as joining

    [Theory]
    [InlineData("2026-03-31", 6.5)]    // Jan-Mar: 3 full months, 25 / 12 * 3 = 6.25 -> 6.5
    [InlineData("2026-03-30", 4.5)]    // March is not complete: 2 months, 4.17 -> 4.5
    [InlineData("2026-01-31", 2.5)]    // 1 month: 2.083 -> 2.5
    [InlineData("2026-01-30", 0)]      // January is not complete
    [InlineData("2026-12-31", 25)]     // the whole year: the entered value
    [InlineData("2026-12-30", 23)]     // 11 months: 22.92 -> 23
    [InlineData("2028-02-29", 4.5)]    // leap day is the last day of February: 2 months, 4.17 -> 4.5
    [InlineData("2028-02-28", 2.5)]    // in a leap year the 28th is not the last day: 1 month
    [InlineData("2026-02-28", 4.5)]    // in a normal year the 28th is the last day: 2 months
    public void On_leaving_the_contractual_days_are_annual_days_divided_by_12_times_the_full_months_rounded_up(string exit, double expected)
    {
        var year = int.Parse(exit[..4], CultureInfo.InvariantCulture);

        VacationEntitlement.Contractual(25, null, year, exit: D(exit)).ShouldBe((decimal)expected);
    }

    [Fact]
    public void After_the_year_of_leaving_there_are_none_and_before_it_the_full_entitlement()
    {
        VacationEntitlement.Contractual(25, null, 2027, exit: D("2026-06-30")).ShouldBe(0m);
        VacationEntitlement.Contractual(25, null, 2025, exit: D("2026-06-30")).ShouldBe(25m);
        VacationEntitlement.Contractual(25, null, 2026, exit: null).ShouldBe(25m);
    }

    [Fact]
    public void Joining_and_leaving_in_the_same_year_count_the_months_between()
    {
        // 1 March to 31 August: March to August, 6 months, 12.5 days
        VacationEntitlement.Contractual(25, D("2026-03-01"), 2026, exit: D("2026-08-31")).ShouldBe(12.5m);
        // joining on the 15th: April to August, 5 months, 10.42 -> 10.5
        VacationEntitlement.Contractual(25, D("2026-03-15"), 2026, exit: D("2026-08-31")).ShouldBe(10.5m);
        // in and out within one month: no full month
        VacationEntitlement.Contractual(25, D("2026-05-01"), 2026, exit: D("2026-05-20")).ShouldBe(0m);
    }

    [Fact]
    public void A_change_during_the_year_and_leaving_are_combined_and_the_rounding_rule_applies()
    {
        // 25 until June (6 months), 20 from July until the end of September (3 months): (150 + 60) / 12 = 17.5
        VacationEntitlement.Contractual(25, null, 2026, null, [new EntitlementChange(Guid.NewGuid(), D("2026-07-01"), 20)], D("2026-09-30")).ShouldBe(17.5m);
        // rounding down instead of up (HR's rule): 25 / 12 * 3 = 6.25 -> 6
        VacationEntitlement.Contractual(25, null, 2026, new VacationRules(null, null, 0, WaitingPeriodMode.Accrued, ProRataRounding.Down, 1m), null, D("2026-03-31")).ShouldBe(6m);
    }

    [Fact]
    public void The_account_of_the_year_of_leaving_uses_the_pro_rata_days()
    {
        var account = VacationAccount.Compute(2026, 25, null, [], VacationRules.NeverExpire, [], D("2026-02-01"), D("2026-02-01"), Calendar, null, D("2026-03-31"));

        (account.ContractualAnnualDays, account.ContractualDays, account.EffectiveDays).ShouldBe((25m, 6.5m, 6.5m));
    }

    // ---------------------------------------------------------------- additional days: window and expiry

    [Fact]
    public void A_carry_over_is_usable_from_the_new_year_until_the_end_of_march()
    {
        var carryOver = Extra(ExtraVacationKind.CarryOver, 2025, 2.5m);

        carryOver.UsableFrom.ShouldBe(D("2026-01-01"));
        carryOver.ExpiresOn(Rules).ShouldBe(D("2026-03-31"));
    }

    [Fact]
    public void Additional_days_are_usable_in_their_year_and_until_the_end_of_march_of_the_next()
    {
        var additional = Extra(ExtraVacationKind.Additional, 2026, 3);

        additional.UsableFrom.ShouldBe(D("2026-01-01"));
        additional.ExpiresOn(Rules).ShouldBe(D("2027-03-31"));
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

        var today = VacationAccount.Compute(2026, 25, null, [carryOver], Rules, [request], D("2026-02-01"), D("2026-02-01"), Calendar);
        var atRequest = VacationAccount.Compute(2026, 25, null, [carryOver], Rules, [request], D("2026-02-01"), D("2026-04-17"), Calendar);

        today.Remaining.ShouldBe(27.5m - 5m);   // the carry-over could still be used by other days until the end of March
        atRequest.Remaining.ShouldBe(25m - 5m); // but for the request itself it is gone
    }

    // ---------------------------------------------------------------- the expiry rule (maintained by HR, default: nothing expires)

    [Fact]
    public void By_default_additional_days_never_expire()
    {
        var carryOver = Extra(ExtraVacationKind.CarryOver, 2025, 2.5m);
        var never = VacationRules.NeverExpire;

        carryOver.ExpiresOn(never).ShouldBeNull();
        var account = VacationAccount.Compute(2030, 25, null, [carryOver], never, [], D("2030-06-01"), D("2030-06-01"), Calendar);

        // Years later the leftover is still there, nothing is forfeited.
        (account.ExtraDays, account.ForfeitedExtraDays, account.EffectiveDays).ShouldBe((2.5m, 0m, 27.5m));
        account.Lines.Single().ExpiresOn.ShouldBeNull();
    }

    [Fact]
    public void Without_an_expiry_the_additional_days_are_still_used_first_and_across_years()
    {
        var additional = Extra(ExtraVacationKind.Additional, 2026, 3);
        var never = VacationRules.NeverExpire;

        // 2 days used in 2026 (Mon 6 and Tue 7 July): one day is left for 2028 and every later year.
        var account = VacationAccount.Compute(2028, 25, null, [additional], never, [new(DateRange.Of(D("2026-07-06"), D("2026-07-07")), AbsenceStatus.Approved)], D("2028-05-01"), D("2028-05-01"), Calendar);

        (account.ExtraDays, account.EffectiveDays).ShouldBe((1m, 26m));
    }

    [Fact]
    public void HR_can_set_another_threshold_for_example_the_end_of_june()
    {
        var june = new VacationRules(6, 30);
        var carryOver = Extra(ExtraVacationKind.CarryOver, 2025, 2.5m);

        carryOver.ExpiresOn(june).ShouldBe(D("2026-06-30"));
        VacationAccount.Compute(2026, 25, null, [carryOver], june, [], D("2026-06-30"), D("2026-06-30"), Calendar).ForfeitedExtraDays.ShouldBe(0m);
        VacationAccount.Compute(2026, 25, null, [carryOver], june, [], D("2026-07-01"), D("2026-07-01"), Calendar).ForfeitedExtraDays.ShouldBe(2.5m);
    }

    [Fact]
    public void A_changed_threshold_applies_to_the_days_that_are_already_entered()
    {
        var carryOver = Extra(ExtraVacationKind.CarryOver, 2025, 2.5m);

        VacationAccount.Compute(2026, 25, null, [carryOver], VacationRules.NeverExpire, [], D("2026-08-01"), D("2026-08-01"), Calendar).EffectiveDays.ShouldBe(27.5m);
        VacationAccount.Compute(2026, 25, null, [carryOver], VacationRules.EndOfFirstQuarter, [], D("2026-08-01"), D("2026-08-01"), Calendar).EffectiveDays.ShouldBe(25m);
    }

    [Fact]
    public void A_leap_day_threshold_falls_on_the_28th_in_years_without_one()
    {
        var february = new VacationRules(2, 29);

        february.ExpiryFor(2027).ShouldBe(D("2028-02-29")); // the year after 2027 is a leap year
        february.ExpiryFor(2025).ShouldBe(D("2026-02-28"));
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData(3, 31, true)]
    [InlineData(2, 29, true)]
    [InlineData(1, 1, true)]
    [InlineData(12, 31, true)]
    [InlineData(3, null, false)]
    [InlineData(null, 31, false)]
    [InlineData(0, 1, false)]
    [InlineData(13, 1, false)]
    [InlineData(4, 31, false)]
    [InlineData(2, 30, false)]
    [InlineData(3, 0, false)]
    public void The_threshold_is_a_real_calendar_day_or_empty(int? month, int? day, bool valid) =>
        new VacationRules(month, day).IsValid.ShouldBe(valid);

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
