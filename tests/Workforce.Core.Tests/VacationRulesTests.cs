using System.Globalization;
using Workforce.Contracts;
using Workforce.Core.Absences;

namespace Workforce.Core.Tests;

/// <summary>What HR can maintain: rounding of the pro rata days, changes of the annual entitlement during a year, the waiting period.</summary>
public class VacationRulesTests
{
    private static readonly WorkCalendar Calendar = new(WorkingWeek.MondayToSaturday, new GermanHolidayCalendar(), null);

    private static DateOnly D(string date) => DateOnly.Parse(date, CultureInfo.InvariantCulture);

    private static EntitlementChange Change(string from, decimal annual) => new(Guid.NewGuid(), D(from), annual);

    // ---------------------------------------------------------------- rounding (default: up to half days)

    [Theory]
    [InlineData(ProRataRounding.Up, 0.5, 22.9167, 23.0)]
    [InlineData(ProRataRounding.Up, 0.5, 22.5, 22.5)]
    [InlineData(ProRataRounding.Up, 1, 22.1, 23.0)]
    [InlineData(ProRataRounding.Nearest, 0.5, 22.9167, 23.0)]
    [InlineData(ProRataRounding.Nearest, 0.5, 22.6, 22.5)]
    [InlineData(ProRataRounding.Nearest, 1, 22.5, 23.0)]
    [InlineData(ProRataRounding.Nearest, 1, 22.4, 22.0)]
    [InlineData(ProRataRounding.Down, 0.5, 22.9167, 22.5)]
    [InlineData(ProRataRounding.Down, 1, 22.9167, 22.0)]
    [InlineData(ProRataRounding.None, 0.5, 22.9167, 22.9167)]
    public void The_rounding_of_pro_rata_days_follows_the_rule(ProRataRounding mode, double step, double days, double expected) =>
        new VacationRules(null, null, 0, WaitingPeriodMode.Accrued, mode, (decimal)step).Round((decimal)days).ShouldBe((decimal)expected);

    [Fact]
    public void The_default_rounding_is_up_to_half_days() =>
        new VacationRules(null, null).Round(2.0833m).ShouldBe(2.5m);

    [Fact]
    public void The_rounding_rule_changes_the_pro_rata_days_in_the_year_of_joining()
    {
        // 25 / 12 * 1 month = 2.083
        VacationEntitlement.Contractual(25, D("2026-12-01"), 2026).ShouldBe(2.5m);
        VacationEntitlement.Contractual(25, D("2026-12-01"), 2026, new VacationRules(null, null, 0, WaitingPeriodMode.Accrued, ProRataRounding.Down, 0.5m)).ShouldBe(2m);
        VacationEntitlement.Contractual(25, D("2026-12-01"), 2026, new VacationRules(null, null, 0, WaitingPeriodMode.Accrued, ProRataRounding.Up, 1m)).ShouldBe(3m);
        VacationEntitlement.Contractual(25, D("2026-12-01"), 2026, new VacationRules(null, null, 0, WaitingPeriodMode.Accrued, ProRataRounding.None, 0.5m)).ShouldBe(25m / 12m);
    }

    [Fact]
    public void A_full_year_without_changes_is_the_entered_value_and_is_not_rounded() =>
        VacationEntitlement.Contractual(22.75m, null, 2026).ShouldBe(22.75m);

    // ---------------------------------------------------------------- the annual entitlement changes during the year

    [Fact]
    public void A_change_on_the_first_of_a_month_applies_from_that_month()
    {
        // 25 days for January to June, 20 days from July: (25 * 6 + 20 * 6) / 12 = 22.5
        VacationEntitlement.Contractual(25, null, 2026, null, [Change("2026-07-01", 20)]).ShouldBe(22.5m);
    }

    [Fact]
    public void A_change_during_a_month_applies_from_the_next_month()
    {
        // from 15 July: January to July 25 (7 months), August to December 20 (5 months): (175 + 100) / 12 = 22.92 -> 23
        VacationEntitlement.Contractual(25, null, 2026, null, [Change("2026-07-15", 20)]).ShouldBe(23m);
    }

    [Fact]
    public void Several_changes_apply_in_the_order_of_their_dates()
    {
        // 30 until March, 25 from April, 20 from October: (30*3 + 25*6 + 20*3) / 12 = 25
        VacationEntitlement.Contractual(30, null, 2026, null, [Change("2026-10-01", 20), Change("2026-04-01", 25)]).ShouldBe(25m);
    }

    [Fact]
    public void Changes_of_other_years_do_not_apply()
    {
        VacationEntitlement.Contractual(25, null, 2026, null, [Change("2025-07-01", 20), Change("2027-01-01", 10)]).ShouldBe(25m);
    }

    [Fact]
    public void A_change_and_joining_in_the_same_year_are_combined()
    {
        // joins on 1 April (April to December, 9 months), 24 days until June (3 months), 30 days from July (6 months): (24*3 + 30*6) / 12 = 21
        VacationEntitlement.Contractual(24, D("2026-04-01"), 2026, null, [Change("2026-07-01", 30)]).ShouldBe(21m);
    }

    [Fact]
    public void The_account_uses_the_changes()
    {
        var account = VacationAccount.Compute(2026, 25, null, [], VacationRules.NeverExpire, [], D("2026-03-01"), D("2026-03-01"), Calendar, [Change("2026-07-01", 20)]);

        (account.ContractualAnnualDays, account.ContractualDays, account.EffectiveDays).ShouldBe((25m, 22.5m, 22.5m));
    }

    // ---------------------------------------------------------------- the waiting period

    private static VacationRules Waiting(int months, WaitingPeriodMode mode) => new(null, null, months, mode);

    private static bool Allows(string entry, VacationRules rules, string[] existing, string[] requested, decimal annual = 24) =>
        VacationWaitingPeriod.Allows(D(entry), rules, _ => annual, existing.Select(D), requested.Select(D));

    [Fact]
    public void Without_a_waiting_period_everything_is_allowed() =>
        Allows("2026-03-01", Waiting(0, WaitingPeriodMode.Blocked), [], ["2026-03-02"]).ShouldBeTrue();

    [Fact]
    public void In_blocked_mode_no_vacation_may_start_before_the_waiting_period_is_over()
    {
        var rules = Waiting(6, WaitingPeriodMode.Blocked);

        Allows("2026-03-01", rules, [], ["2026-08-31"]).ShouldBeFalse();
        Allows("2026-03-01", rules, [], ["2026-09-01"]).ShouldBeTrue(); // six months after the entry
        Allows("2026-03-01", rules, [], ["2026-08-29", "2026-09-02"]).ShouldBeFalse();
    }

    [Fact]
    public void In_accrued_mode_only_the_days_earned_so_far_may_be_taken()
    {
        var rules = Waiting(6, WaitingPeriodMode.Accrued);

        // Entry 1 March, 24 days a year = 2 days per month. On 4 May two months are complete: 4 days are earned.
        Allows("2026-03-01", rules, [], ["2026-05-04", "2026-05-05", "2026-05-06", "2026-05-07"]).ShouldBeTrue();
        Allows("2026-03-01", rules, [], ["2026-05-04", "2026-05-05", "2026-05-06", "2026-05-07", "2026-05-08"]).ShouldBeFalse();
    }

    [Fact]
    public void In_accrued_mode_the_days_already_booked_count()
    {
        var rules = Waiting(6, WaitingPeriodMode.Accrued);

        // 24 days a year, two months complete on 4 May: 4 days earned.
        Allows("2026-03-01", rules, ["2026-04-06", "2026-04-07"], ["2026-05-04", "2026-05-05"]).ShouldBeTrue();                 // 2 booked + 2 = 4
        Allows("2026-03-01", rules, ["2026-04-06", "2026-04-07"], ["2026-05-04", "2026-05-05", "2026-05-06"]).ShouldBeFalse();  // 2 + 3 = 5
        Allows("2026-03-01", rules, ["2026-04-06", "2026-04-07", "2026-04-08"], ["2026-05-04", "2026-05-05"]).ShouldBeFalse();  // 3 + 2 = 5
    }

    [Fact]
    public void A_month_is_complete_on_the_day_of_the_month_of_the_entry()
    {
        var rules = Waiting(6, WaitingPeriodMode.Accrued);

        // Entry 15 March: on 14 April no month is complete, on 15 April one month (2 days).
        Allows("2026-03-15", rules, [], ["2026-04-14"]).ShouldBeFalse();
        Allows("2026-03-15", rules, [], ["2026-04-15"]).ShouldBeTrue();
        Allows("2026-03-15", rules, [], ["2026-04-15", "2026-04-16", "2026-04-17"]).ShouldBeFalse();
    }

    [Fact]
    public void After_the_waiting_period_there_is_no_further_restriction_from_it()
    {
        var rules = Waiting(6, WaitingPeriodMode.Accrued);

        Allows("2026-03-01", rules, [], ["2026-09-01", "2026-09-02", "2026-09-03", "2026-09-04", "2026-09-05", "2026-09-07", "2026-09-08"]).ShouldBeTrue();
    }

    [Fact]
    public void An_unknown_entitlement_does_not_block_here_the_balance_check_reports_it()
    {
        VacationWaitingPeriod.Allows(D("2026-03-01"), Waiting(6, WaitingPeriodMode.Accrued), _ => null, [], [D("2026-04-06")]).ShouldBeTrue();
    }

    // ---------------------------------------------------------------- the rules as a whole

    [Theory]
    [InlineData(0, WaitingPeriodMode.Accrued, ProRataRounding.Up, 0.5, true)]
    [InlineData(6, WaitingPeriodMode.Blocked, ProRataRounding.Nearest, 1, true)]
    [InlineData(24, WaitingPeriodMode.Accrued, ProRataRounding.None, 0.5, true)]
    [InlineData(-1, WaitingPeriodMode.Accrued, ProRataRounding.Up, 0.5, false)]
    [InlineData(25, WaitingPeriodMode.Accrued, ProRataRounding.Up, 0.5, false)]
    [InlineData(6, WaitingPeriodMode.Unknown, ProRataRounding.Up, 0.5, false)]
    [InlineData(6, WaitingPeriodMode.Accrued, ProRataRounding.Unknown, 0.5, false)]
    [InlineData(6, WaitingPeriodMode.Accrued, ProRataRounding.Up, 0.25, false)]
    [InlineData(6, WaitingPeriodMode.Accrued, ProRataRounding.Up, 2, false)]
    public void The_waiting_period_and_the_rounding_must_be_sensible(int months, WaitingPeriodMode mode, ProRataRounding rounding, double step, bool valid) =>
        new VacationRules(null, null, months, mode, rounding, (decimal)step).IsValid.ShouldBe(valid);
}
