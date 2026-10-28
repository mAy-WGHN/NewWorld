using Workforce.Core.WorkingTime;

namespace Workforce.Core.Tests;

/// <summary>The balance of the working time account: first in, first out, expiry by the earning date, deficit before new overtime.</summary>
public class WorkingTimeAccountTests
{
    private static readonly Guid Erika = Guid.Parse("00000000-0000-0000-0000-0000000000e1");

    private static DateOnly D(string date) => DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture);

    private static TimeBooking B(string date, decimal hours, BookingKind kind = BookingKind.Correction, int order = 0) => new()
    {
        Id = Guid.NewGuid(), EmployeeId = Erika, Kind = kind, Hours = hours, Date = D(date), CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, order, TimeSpan.Zero),
    };

    private static readonly WorkingTimeRules NeverExpire = WorkingTimeRules.Default;

    private static WorkingTimeRules ExpireAfter(int months) => new(0, months);

    [Fact]
    public void An_empty_account_is_zero()
    {
        var state = WorkingTimeAccount.Compute([], NeverExpire, D("2026-10-28"));

        (state.Balance, state.Forfeited, state.Deficit).ShouldBe((0m, 0m, 0m));
        state.Buckets.ShouldBeEmpty();
    }

    [Fact]
    public void Plus_and_minus_bookings_add_up()
    {
        var state = WorkingTimeAccount.Compute([B("2026-01-31", 12, BookingKind.Overtime), B("2026-02-10", -5, BookingKind.TimeOff), B("2026-02-28", 3.5m, BookingKind.Overtime)], NeverExpire, D("2026-10-28"));

        state.Balance.ShouldBe(10.5m);
        state.Deficit.ShouldBe(0m);
    }

    [Fact]
    public void Time_off_uses_the_oldest_hours_first()
    {
        var state = WorkingTimeAccount.Compute([B("2026-01-31", 10), B("2026-02-28", 10), B("2026-03-05", -12, BookingKind.TimeOff)], NeverExpire, D("2026-10-28"));

        state.Buckets.Select(b => (b.EarnedOn, b.Remaining)).ShouldBe([(D("2026-02-28"), 8m)]);
    }

    [Fact]
    public void More_taken_than_available_is_a_deficit_and_later_hours_cover_it_first()
    {
        var state = WorkingTimeAccount.Compute([B("2026-01-31", 5), B("2026-02-10", -8), B("2026-03-31", 4)], NeverExpire, D("2026-10-28"));

        state.Deficit.ShouldBe(0m);
        state.Buckets.Select(b => (b.EarnedOn, b.Remaining)).ShouldBe([(D("2026-03-31"), 1m)]);
        state.Balance.ShouldBe(1m);
    }

    [Fact]
    public void A_remaining_deficit_makes_the_balance_negative()
    {
        var state = WorkingTimeAccount.Compute([B("2026-01-31", 2), B("2026-02-10", -8)], NeverExpire, D("2026-10-28"));

        (state.Balance, state.Deficit).ShouldBe((-6m, 6m));
    }

    [Fact]
    public void Nothing_expires_without_an_expiry_rule() =>
        WorkingTimeAccount.Compute([B("2020-01-31", 10)], NeverExpire, D("2040-01-01")).Balance.ShouldBe(10m);

    [Fact]
    public void Hours_expire_the_day_after_the_earning_date_plus_the_period_and_are_reported_as_forfeited()
    {
        var bookings = new[] { B("2026-03-31", 10, BookingKind.Overtime) }; // 3 months: usable until 2026-06-30

        var onLastDay = WorkingTimeAccount.Compute(bookings, ExpireAfter(3), D("2026-06-30"));
        var nextDay = WorkingTimeAccount.Compute(bookings, ExpireAfter(3), D("2026-07-01"));

        (onLastDay.Balance, onLastDay.Forfeited).ShouldBe((10m, 0m));
        (nextDay.Balance, nextDay.Forfeited).ShouldBe((0m, 10m));
    }

    [Fact]
    public void The_expiry_date_clamps_to_the_month_end_in_leap_and_normal_years()
    {
        var jan31 = new[] { B("2028-01-31", 10) };
        var jan31Normal = new[] { B("2027-01-31", 10) };

        WorkingTimeAccount.Compute(jan31, ExpireAfter(1), D("2028-02-29")).Balance.ShouldBe(10m); // leap year: 29 Feb is the last day
        WorkingTimeAccount.Compute(jan31, ExpireAfter(1), D("2028-03-01")).Balance.ShouldBe(0m);
        WorkingTimeAccount.Compute(jan31Normal, ExpireAfter(1), D("2027-02-28")).Balance.ShouldBe(10m);
        WorkingTimeAccount.Compute(jan31Normal, ExpireAfter(1), D("2027-03-01")).Balance.ShouldBe(0m);
    }

    [Fact]
    public void The_expiry_period_runs_across_the_year_end()
    {
        var state = WorkingTimeAccount.Compute([B("2026-11-30", 10)], ExpireAfter(3), D("2027-02-28"));

        state.Balance.ShouldBe(10m);
        WorkingTimeAccount.Compute([B("2026-11-30", 10)], ExpireAfter(3), D("2027-03-01")).Forfeited.ShouldBe(10m);
    }

    [Fact]
    public void Hours_taken_before_they_expire_are_used_up_not_forfeited()
    {
        var state = WorkingTimeAccount.Compute([B("2026-01-31", 10), B("2026-02-15", -10, BookingKind.TimeOff)], ExpireAfter(3), D("2026-12-31"));

        (state.Balance, state.Forfeited).ShouldBe((0m, 0m));
    }

    [Fact]
    public void Time_off_cannot_use_hours_that_expired_before_it()
    {
        // Overtime of January expired in April; time off in May takes the hours of March first, the rest becomes a deficit.
        var state = WorkingTimeAccount.Compute([B("2026-01-31", 10), B("2026-03-31", 4), B("2026-05-10", -6, BookingKind.TimeOff)], ExpireAfter(3), D("2026-05-10"));

        (state.Forfeited, state.Deficit, state.Balance).ShouldBe((10m, 2m, -2m));
    }

    [Fact]
    public void Bookings_after_the_reference_day_do_not_count_yet()
    {
        var state = WorkingTimeAccount.Compute([B("2026-10-28", 5), B("2026-10-29", -5, BookingKind.TimeOff)], NeverExpire, D("2026-10-28"));

        state.Balance.ShouldBe(5m);
    }

    [Fact]
    public void The_order_of_bookings_on_one_day_follows_their_creation()
    {
        var state = WorkingTimeAccount.Compute([B("2026-02-10", -3, BookingKind.TimeOff, 2), B("2026-02-10", 3, BookingKind.Correction, 1)], NeverExpire, D("2026-10-28"));

        state.Balance.ShouldBe(0m); // the plus (created first) is there when the minus is taken: no deficit
        state.Deficit.ShouldBe(0m);
    }

    [Theory]
    [InlineData(0, null, true)]
    [InlineData(40, 12, true)]
    [InlineData(-1, null, false)]
    [InlineData(1001, null, false)]
    [InlineData(0, 0, false)]
    [InlineData(0, 121, false)]
    public void Rules_have_sensible_limits(double payout, int? months, bool valid) =>
        new WorkingTimeRules((decimal)payout, months).IsValid.ShouldBe(valid);
}
